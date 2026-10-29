using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Assistant;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Assistant;

internal sealed class AssistantStore(DbContextOptions<AppDbContext> options, TimeProvider clock) : IAssistantStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const int MaximumTurns = 10;
    private static IQueryable<AIConversation> Scoped(AppDbContext db, AssistantIdentity caller)
    {
        var ids = caller.TicketIds.ToArray();
        return db.AIConversations.Where(c => c.UserId == caller.UserId && c.TicketId.HasValue && (caller.AllTickets || ids.Contains(c.TicketId.Value)));
    }
    private static async Task Active(AppDbContext db, AssistantIdentity caller, CancellationToken ct, bool locked = false)
    {
        var user = locked
            ? await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM app.users WHERE id = {caller.UserId} FOR UPDATE").SingleOrDefaultAsync(ct)
            : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == caller.UserId, ct);
        if (user?.IsActive != true) throw new AssistantAccessDeniedException();
    }
    public async Task<AssistantTicket?> FindTicketAsync(Guid? id, string? reference, AssistantIdentity caller, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await Active(db, caller, ct);
        var ids = caller.TicketIds.ToArray();
        var query = db.Tickets.AsNoTracking().Where(t => caller.AllTickets || ids.Contains(t.Id));
        return await query.Where(t => id.HasValue ? t.Id == id : t.Reference == reference)
            .Select(t => new AssistantTicket(t.Id, t.Reference)).SingleOrDefaultAsync(ct);
    }
    public async Task<AssistantConversation> CreateAsync(Guid id, AssistantTicket ticket, string title, AssistantIdentity caller, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Active(db, caller, ct, true);
        var existing = await db.AIConversations.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (existing is not null)
        {
            if (existing.UserId != caller.UserId || !caller.CanRead(existing.TicketId ?? Guid.Empty)) throw new ResourceNotFoundException("Conversation");
            if (existing.TicketId != ticket.Id || existing.Title != title) throw new ConflictException("The request identifier already belongs to a different conversation.");
        }
        else
        {
            if (!caller.CanRead(ticket.Id)) throw new ResourceNotFoundException("Ticket");
            var now = clock.GetUtcNow();
            db.AIConversations.Add(new AIConversation(id, caller.UserId, now, ticket.Id, title));
            db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), caller.UserId, "Assistant.Conversation.Created", "AIConversation", id, now));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, caller, ct);
    }
    public async Task<AssistantConversationPage> ListAsync(AssistantIdentity caller, int page, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await Active(db, caller, ct);
        var items = await (from c in Scoped(db, caller).AsNoTracking() join t in db.Tickets on c.TicketId equals t.Id
            orderby c.UpdatedAt descending, c.Id descending
            select new AssistantConversationSummary(c.Id, c.Title ?? t.Reference, t.Id, t.Reference, c.UpdatedAt))
            .Skip((page - 1) * 20).Take(21).ToListAsync(ct);
        return new(items.Take(20).ToArray(), page, items.Count > 20);
    }
    public async Task<AssistantConversation> GetAsync(Guid id, AssistantIdentity caller, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await Active(db, caller, ct);
        var conversation = await Scoped(db, caller).AsNoTracking().Include(c => c.Messages.OrderBy(m => EF.Property<int>(m, "Position"))).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ResourceNotFoundException("Conversation");
        var ticket = await db.Tickets.AsNoTracking().Where(t => t.Id == conversation.TicketId).Select(t => new { t.Reference, t.Revision }).SingleAsync(ct);
        var turns = await db.AssistantTurns.AsNoTracking().Where(t => t.ConversationId == id).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);
        var runIds = turns.Select(t => t.RunId).ToArray();
        var analyses = await db.AgentAnalyses.AsNoTracking().Where(a => runIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.CorrelationId != null && runIds.Contains(a.CorrelationId.Value) && a.Action.StartsWith("Agent.Tool."))
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(ct);
        var messages = conversation.Messages.ToDictionary(m => m.Id);
        var output = new List<AssistantTurn>();
        foreach (var turn in turns)
        {
            var expired = turn.State == "Pending" && turn.LeaseExpiresAt < clock.GetUtcNow();
            TicketAgentResult? result = null;
            if (turn.State == "Completed" && analyses.TryGetValue(turn.RunId, out var saved))
            {
                result = JsonSerializer.Deserialize<TicketAgentResult>(saved.ResultJson, Json) ?? throw new InvalidOperationException("Invalid saved assistant result.");
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, Json))));
                if (hash != saved.ResultHash || result.RunId != turn.RunId || result.Facts.Id != conversation.TicketId) throw new InvalidOperationException("Invalid assistant result provenance.");
            }
            var activities = new Dictionary<string, AssistantActivity>();
            foreach (var entry in audit.Where(a => a.CorrelationId == turn.RunId))
            {
                using var detail = JsonDocument.Parse(entry.Details ?? "{}");
                if (!detail.RootElement.TryGetProperty("metadata", out var meta) || !meta.TryGetProperty("callId", out var call)) continue;
                var name = meta.GetProperty("name").GetString() ?? "unrecognized";
                if (name is not ("get_ticket" or "get_ticket_history" or "search_knowledge" or "get_team" or "get_team_information" or "search_tickets" or "get_system_status" or "unrecognized")) continue;
                var callId = call.GetString() ?? "";
                var state = entry.Action["Agent.Tool.".Length..];
                // Timestamps may tie. A terminal audit takes precedence over Started.
                if (!activities.TryGetValue(callId, out var old) || old.State == "Started" || state != "Started")
                    activities[callId] = new(callId, name, state, entry.OccurredAt);
            }
            AssistantMessage Message(Guid messageId) { var m = messages[messageId]; return new(m.Id, m.Role.ToString(), m.Content, m.CreatedAt); }
            output.Add(new(turn.Id, turn.RunId, expired ? "Interrupted" : turn.State, Message(turn.UserMessageId),
                turn.AssistantMessageId.HasValue ? Message(turn.AssistantMessageId.Value) : null, result,
                expired ? "Interrupted" : turn.ErrorCode, activities.Values.OrderBy(a => a.OccurredAt).ThenBy(a => a.CallId).ToArray(), turn.CreatedAt, turn.UpdatedAt));
        }
        return new(conversation.Id, conversation.TicketId!.Value, ticket.Reference, ticket.Revision, conversation.Title ?? ticket.Reference, conversation.CreatedAt, conversation.UpdatedAt, output);
    }
    public async Task<AssistantTurnStart> BeginAsync(Guid conversationId, Guid requestId, string content, AssistantIdentity caller, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Active(db, caller, ct, true);
        var conversation = await Scoped(db, caller).Include(c => c.Messages.OrderBy(m => EF.Property<int>(m, "Position"))).SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new ResourceNotFoundException("Conversation");
        var existing = await db.AssistantTurns.SingleOrDefaultAsync(t => t.Id == requestId, ct);
        if (existing is not null)
        {
            if (existing.ConversationId != conversationId) throw new ConflictException("The request identifier is already in use.");
            if (conversation.Messages.Single(m => m.Id == existing.UserMessageId).Content != content) throw new ConflictException("A request identifier cannot be reused with another message.");
            await tx.CommitAsync(ct);
            return new(false, existing.RunId, conversation.TicketId!.Value, content, []);
        }
        var now = clock.GetUtcNow();
        var pending = await db.AssistantTurns.Where(t => t.ConversationId == conversationId && t.State == "Pending").ToListAsync(ct);
        if (pending.Any(t => t.LeaseExpiresAt >= now)) throw new ConflictException("An assistant response is already in progress.");
        foreach (var abandoned in pending) Fail(db, conversation, abandoned, "Interrupted", now);
        if (await db.AssistantTurns.CountAsync(t => t.ConversationId == conversationId, ct) >= MaximumTurns)
            throw new ConflictException("This conversation has reached its limit. Start a new conversation.");
        var message = new AIMessage(Guid.NewGuid(), conversationId, AIMessageRole.User, content, now);
        var memory = new List<AgentMessage>();
        foreach (var previous in conversation.Messages.Where(m => m.Role is AIMessageRole.User or AIMessageRole.Assistant).TakeLast(4))
            memory.Add(new(previous.Role == AIMessageRole.User ? "user" : "assistant", previous.Content[..Math.Min(1900, previous.Content.Length)]));
        conversation.AddMessage(message);
        var turn = new AssistantTurnRecord { Id = requestId, ConversationId = conversationId, RunId = Guid.NewGuid(), UserMessageId = message.Id,
            CreatedAt = now, UpdatedAt = now, LeaseExpiresAt = now.AddSeconds(240) };
        db.AssistantTurns.Add(turn);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), caller.UserId, "Assistant.Message.Accepted", "AIConversation", conversationId, now, correlationId: turn.RunId));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(true, turn.RunId, conversation.TicketId!.Value, content, memory);
    }
    public async Task FinishAsync(Guid conversationId, Guid requestId, TicketAgentResult? result, string? errorCode, AssistantIdentity caller, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Active(db, caller, ct, true);
        var conversation = await Scoped(db, caller).Include(c => c.Messages.OrderBy(m => EF.Property<int>(m, "Position"))).SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new ResourceNotFoundException("Conversation");
        var turn = await db.AssistantTurns.SingleAsync(t => t.Id == requestId && t.ConversationId == conversationId, ct);
        if (turn.State != "Pending") return;
        var now = clock.GetUtcNow();
        if (result is null) Fail(db, conversation, turn, errorCode ?? "DependencyUnavailable", now);
        else
        {
            if (result.RunId != turn.RunId || result.Facts.Id != conversation.TicketId || !await db.AgentAnalyses.AnyAsync(a => a.Id == result.RunId && a.TicketId == conversation.TicketId, ct))
                throw new InvalidOperationException("Assistant result must reference the saved investigation.");
            static string Clip(string text, int maximum) => text[..Math.Min(maximum, text.Length)];
            var memory = "Summary: " + Clip(result.Summary, 600) + "\nHypothesis: " + Clip(result.Diagnosis, 350) +
                "\nPROPOSED ACTIONS, NOT EXECUTED:\n" + string.Join("\n", result.RecommendedActions.Take(3).Select(a => Clip(a.Description, 250)));
            var message = new AIMessage(Guid.NewGuid(), conversationId, AIMessageRole.Assistant, memory, now);
            conversation.AddMessage(message); turn.AssistantMessageId = message.Id; turn.AnalysisId = result.RunId; turn.State = "Completed"; turn.UpdatedAt = now;
            db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), caller.UserId, "Assistant.Message.Completed", "AIConversation", conversationId, now, correlationId: turn.RunId));
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private static void Fail(AppDbContext db, AIConversation conversation, AssistantTurnRecord turn, string code, DateTimeOffset now)
    {
        var message = new AIMessage(Guid.NewGuid(), conversation.Id, AIMessageRole.Assistant, "The assistant could not produce a validated response. Please try again.", now);
        conversation.AddMessage(message); turn.AssistantMessageId = message.Id; turn.State = "Failed"; turn.ErrorCode = code; turn.UpdatedAt = now;
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), conversation.UserId, "Assistant.Message.Failed", "AIConversation", conversation.Id, now, code, turn.RunId));
    }
}
