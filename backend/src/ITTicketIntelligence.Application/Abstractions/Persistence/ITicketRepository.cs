using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;

namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public interface ITicketRepository
{
    /// <summary>Loads the tracked aggregate, including its comments and predictions.</summary>
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(Ticket ticket);
    Task<string> NextReferenceAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<TicketSummaryDto>> GetPageAsync(GetTicketsQuery query, CancellationToken cancellationToken = default);
}
