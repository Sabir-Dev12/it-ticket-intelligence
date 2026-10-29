namespace ITTicketIntelligence.Application.Security;
public sealed record AdminTeam(Guid Id,string Name,bool IsActive,Guid[] MemberIds);
public sealed record AuditEntry(Guid Id,Guid? UserId,string Action,string EntityType,Guid EntityId,DateTimeOffset OccurredAt);
public interface IAdminDirectory
{
    Task<IReadOnlyList<AdminTeam>> TeamsAsync(CancellationToken ct);
    Task<AdminTeam> CreateTeamAsync(Guid actor,string name,string? description,CancellationToken ct);
    Task MembershipAsync(Guid actor,Guid team,Guid user,bool add,CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> AuditAsync(int page,CancellationToken ct);
}
