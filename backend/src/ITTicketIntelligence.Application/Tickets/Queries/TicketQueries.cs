using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Tickets.Queries;

public sealed record GetTicketQuery(Guid Id);
public sealed record GetTicketsQuery(int Page = 1, int PageSize = 20, string? Search = null,
    TicketStatus? Status = null, TicketPriority? Priority = null, TicketCategory? Category = null,
    Guid? AssignedTeamId = null, Guid? CreatedById = null, bool IncludeArchived = false, Guid[]? AllowedTicketIds = null);
