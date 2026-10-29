using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
namespace ITTicketIntelligence.Infrastructure.Persistence;
internal sealed class AdminDirectory(AppDbContext db) : IAdminDirectory
{
    public async Task<IReadOnlyList<AdminTeam>> TeamsAsync(CancellationToken ct) => await db.Teams.AsNoTracking().OrderBy(t=>t.Name).Take(200).Select(t=>new AdminTeam(t.Id,t.Name,t.IsActive,t.Members.Select(m=>m.UserId).ToArray())).ToListAsync(ct);
    public async Task<AdminTeam> CreateTeamAsync(Guid actor,string name,string? description,CancellationToken ct)
    { var team=new Team(Guid.NewGuid(),name,DateTimeOffset.UtcNow,description);db.Teams.Add(team);db.AuditLogs.Add(new AuditLog(Guid.NewGuid(),actor,"Admin.Team.Created","Team",team.Id,DateTimeOffset.UtcNow));await db.SaveChangesAsync(ct);return new(team.Id,team.Name,team.IsActive,[]); }
    public async Task MembershipAsync(Guid actor,Guid team,Guid user,bool add,CancellationToken ct)
    {
        var entity=await db.Teams.Include(t=>t.Members).SingleOrDefaultAsync(t=>t.Id==team,ct)??throw new ResourceNotFoundException("Team");
        if(!await db.Users.AnyAsync(u=>u.Id==user && (!add || u.IsActive),ct))throw new ResourceNotFoundException("User");
        if(add)entity.AddMember(user,DateTimeOffset.UtcNow);else entity.RemoveMember(user,DateTimeOffset.UtcNow);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(),actor,add?"Admin.Team.MemberAdded":"Admin.Team.MemberRemoved","Team",team,DateTimeOffset.UtcNow, user.ToString()));await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(int page,CancellationToken ct)=>await db.AuditLogs.AsNoTracking().OrderByDescending(a=>a.OccurredAt).ThenBy(a=>a.Id).Skip((page-1)*50).Take(50).Select(a=>new AuditEntry(a.Id,a.UserId,a.Action,a.EntityType,a.EntityId,a.OccurredAt)).ToListAsync(ct);
}
