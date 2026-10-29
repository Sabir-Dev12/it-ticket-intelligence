using ITTicketIntelligence.Application.Agents;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Agents;

internal sealed class TicketAgentData(AppDbContext db) : ITicketAgentData
{
    public Task<AgentTicketFacts?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).Select(t => new AgentTicketFacts(t.Id, t.Revision,
            t.Title, t.Description, t.Status, t.IsArchived, t.AssignedTeamId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentHistoryEntry>> GetHistoryAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var comments = await db.TicketComments.AsNoTracking().Where(c => c.TicketId == ticketId)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Take(10)
            .Select(c => new AgentHistoryEntry("ReportedComment", c.CreatedAt, c.Message.Substring(0, Math.Min(c.Message.Length, 500))))
            .ToListAsync(cancellationToken);
        var predictions = await db.TicketPredictions.AsNoTracking().Where(p => p.TicketId == ticketId)
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Take(6).ToListAsync(cancellationToken);
        comments.AddRange(predictions.Select(p => new AgentHistoryEntry("MLPrediction", p.CreatedAt,
            $"{p.Type}: {p.PredictedCategory?.ToString() ?? p.PredictedPriority?.ToString() ?? p.RecommendedTeamName ?? "Abstained"}; input revision {p.InputRevision}; model {p.ModelVersion}; confidence {p.Confidence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}; review {p.NeedsHumanReview}")));
        string[] ticketActions = ["TicketCreated", "TicketUpdated", "TicketStatusChanged", "TicketAssigned", "TicketCommentAdded", "TicketArchived", "TicketRestored"];
        var changes = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId == ticketId && a.EntityType == "Ticket" && ticketActions.Contains(a.Action))
            .OrderByDescending(a => a.OccurredAt).ThenBy(a => a.Id).Take(10)
            .Select(a => new AgentHistoryEntry("RecordedChange", a.OccurredAt, a.Action)).ToListAsync(cancellationToken);
        comments.AddRange(changes);
        return comments.OrderByDescending(e => e.OccurredAt).Take(26).ToArray();
    }

    public Task<AgentTeamInformation?> GetTeamAsync(string name, CancellationToken cancellationToken) =>
        db.Teams.AsNoTracking().Where(t => t.Name == name).Select(t => new AgentTeamInformation(t.Id,
            t.Name, t.Description, t.IsActive, t.Members.Count)).SingleOrDefaultAsync(cancellationToken);
}
