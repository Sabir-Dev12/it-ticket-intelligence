using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;

namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public sealed record TicketAnalysisInput(Guid Id, string Title, string Description, int Revision,
    TicketStatus Status, bool IsArchived);

public interface ITicketIntelligenceStore
{
    Task<TicketAnalysisInput?> GetInputAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>Appends one complete analysis atomically, only if the ticket revision is still current and active.</summary>
    Task SaveAsync(Guid ticketId, int inputRevision, IReadOnlyList<TicketPrediction> predictions,
        CancellationToken cancellationToken = default);
}
