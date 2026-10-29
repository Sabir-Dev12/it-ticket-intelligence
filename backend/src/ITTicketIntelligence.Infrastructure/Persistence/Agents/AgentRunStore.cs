using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Agents;

// Short independent contexts make each audit durable even if a later stage fails or is cancelled.
internal sealed class AgentRunStore(DbContextOptions<AppDbContext> options) : IAgentRunStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public async Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken)
    {
        await using var db = new AppDbContext(options);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, entry.Action, "TicketAgent", entry.TicketId,
            entry.OccurredAt, entry.Details, entry.RunId));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(TicketAgentResult result, string provider, string model, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var id = result.Facts.Id;
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT *, xmin FROM app.tickets WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (ticket is null || ticket.Revision != result.TicketRevision || ticket.IsArchived ||
            ticket.Status is not (TicketStatus.Open or TicketStatus.InProgress))
            throw new ConflictException("The ticket changed during investigation. Retry using the current ticket.");
        foreach (var documentId in result.Sources.Select(s => s.DocumentId).Distinct().Order())
        {
            var document = await db.KnowledgeDocuments.FromSqlInterpolated($"SELECT *, xmin FROM app.knowledge_documents WHERE id = {documentId} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (document is null || result.Sources.Where(s => s.DocumentId == documentId).Any(s =>
                s.DocumentVersion != document.Version || s.Title != document.Title || s.Source != document.Source))
                throw new ConflictException("A cited knowledge document changed during investigation. Retry retrieval.");
        }
        var chunkIds = result.Sources.Select(s => s.ChunkId).ToArray();
        var current = await db.KnowledgeChunks.AsNoTracking().Where(c => chunkIds.Contains(c.Id)).ToListAsync(cancellationToken);
        if (result.Sources.Any(s => !current.Any(c => c.Id == s.ChunkId && c.DocumentId == s.DocumentId &&
            c.DocumentVersion == s.DocumentVersion && c.ContentHash == s.ContentHash)))
            throw new ConflictException("A cited knowledge chunk changed during investigation. Retry retrieval.");
        var json = JsonSerializer.Serialize(result, Json);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        db.AgentAnalyses.Add(new AgentAnalysisRecord { Id = result.RunId, TicketId = id, TicketRevision = result.TicketRevision,
            ResultJson = json, ResultHash = hash, Provider = provider, Model = model, CreatedAt = occurredAt, UpdatedAt = occurredAt });
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, "Agent.Run.Completed", "TicketAgent", id, occurredAt,
            JsonSerializer.Serialize(new { resultHash = hash, result.TicketRevision, provider, model,
                sourceIds = chunkIds, actions = result.RecommendedActions.Count, requiresHumanReview = true }, Json), result.RunId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
