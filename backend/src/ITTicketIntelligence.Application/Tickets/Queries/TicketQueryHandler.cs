using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Validation;

namespace ITTicketIntelligence.Application.Tickets.Queries;

public sealed class TicketQueryHandler(ITicketRepository tickets, ITicketReferenceData? references = null) :
    IQueryHandler<GetTicketQuery, TicketDto>, IQueryHandler<GetTicketsQuery, PagedResult<TicketSummaryDto>>
{
    public async Task<TicketDto> HandleAsync(GetTicketQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Id(query.Id, "id").ThrowIfInvalid();
        var ticket = await tickets.GetByIdAsync(query.Id, cancellationToken) ?? throw new ResourceNotFoundException("Ticket");
        var dto = ticket.ToDto();
        return ticket.AssignedUserId is { } userId && references is not null
            ? dto with { AssignedUserName = (await references.FindUserAsync(userId, cancellationToken))?.Name } : dto;
    }

    public Task<PagedResult<TicketSummaryDto>> HandleAsync(GetTicketsQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Require(query.Page is >= 1 and <= 1000000, "page", "La page doit être comprise entre 1 et 1 000 000.")
            .Require(query.PageSize is >= 1 and <= 100, "pageSize", "La taille de page doit être comprise entre 1 et 100.")
            .OptionalText(query.Search, "search", 200).OptionalEnum(query.Status, "status")
            .OptionalEnum(query.Category, "category").OptionalEnum(query.Priority, "priority")
            .OptionalId(query.AssignedTeamId, "assignedTeamId").OptionalId(query.CreatedById, "createdById")
            .ThrowIfInvalid();
        return tickets.GetPageAsync(query, cancellationToken);
    }
}
