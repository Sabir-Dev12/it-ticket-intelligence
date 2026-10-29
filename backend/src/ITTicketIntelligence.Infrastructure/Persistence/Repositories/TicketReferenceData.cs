using ITTicketIntelligence.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class TicketReferenceData(AppDbContext db) : ITicketReferenceData
{
    public Task<TicketUserReference?> FindUserAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().Where(user => user.Id == id).Select(user => new TicketUserReference(user.Id, user.IsActive, user.Role, user.Name))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<TicketTeamReference?> FindTeamAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Teams.AsNoTracking().Where(team => team.Id == id).Select(team => new TicketTeamReference(team.Id, team.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}
