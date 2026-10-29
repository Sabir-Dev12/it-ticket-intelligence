using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using System.Globalization;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class TicketRepository(AppDbContext db) : ITicketRepository
{
    public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Tickets.Include(ticket => ticket.Comments).Include(ticket => ticket.Predictions)
            .AsSplitQuery().SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    public void Add(Ticket ticket) => db.Tickets.Add(ticket);

    public async Task<string> NextReferenceAsync(CancellationToken cancellationToken = default)
    {
        var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('app.ticket_reference_sequence') AS \"Value\"").SingleAsync(cancellationToken);
        return "IT-" + number.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<PagedResult<TicketSummaryDto>> GetPageAsync(GetTicketsQuery filter, CancellationToken cancellationToken = default)
    {
        var query = db.Tickets.AsNoTracking().AsQueryable();
        if (filter.AllowedTicketIds is not null) query = query.Where(t => filter.AllowedTicketIds.Contains(t.Id));
        if (!filter.IncludeArchived) query = query.Where(ticket => !ticket.IsArchived);
        if (filter.Status.HasValue) query = query.Where(ticket => ticket.Status == filter.Status);
        if (filter.Priority.HasValue) query = query.Where(ticket => ticket.Priority == filter.Priority);
        if (filter.Category.HasValue) query = query.Where(ticket => ticket.Category == filter.Category);
        if (filter.AssignedTeamId.HasValue) query = query.Where(ticket => ticket.AssignedTeamId == filter.AssignedTeamId);
        if (filter.CreatedById.HasValue) query = query.Where(ticket => ticket.CreatedById == filter.CreatedById);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = "%" + filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(ticket => EF.Functions.ILike(ticket.Title, search, "\\") ||
                EF.Functions.ILike(ticket.Description, search, "\\") || EF.Functions.ILike(ticket.Reference, search, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(ticket => ticket.CreatedAt).ThenByDescending(ticket => ticket.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(ticket => new TicketSummaryDto(ticket.Id, ticket.Reference, ticket.Title, ticket.Description,
                ticket.CreatedById, ticket.Status, ticket.Category, ticket.Priority, ticket.AssignedTeamId,
                ticket.Revision, ticket.CreatedAt, ticket.UpdatedAt, ticket.IsArchived, ticket.ArchivedAt,
                ticket.Comments.Count, ticket.Predictions.Count)).ToListAsync(cancellationToken);
        return new PagedResult<TicketSummaryDto>(items, total, filter.Page, filter.PageSize);
    }
}
