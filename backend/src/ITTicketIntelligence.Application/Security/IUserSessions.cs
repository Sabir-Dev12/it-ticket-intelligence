using ITTicketIntelligence.Domain.Enums;
namespace ITTicketIntelligence.Application.Security;
public sealed record SessionUser(Guid Id, string Name, string Email, UserRole Role, string Version, Guid[] TicketIds);
public sealed record UserAccount(Guid Id, string Name, string Email, UserRole Role, bool IsActive);
public interface IUserSessions
{
    Task<SessionUser?> LoginAsync(string email, string password, CancellationToken ct);
    Task<SessionUser?> FindAsync(Guid id, CancellationToken ct);
    Task AuditAsync(Guid? userId, string action, CancellationToken ct);
    Task BootstrapAsync(string email, string password, CancellationToken ct);
    Task RevokeAsync(Guid id, CancellationToken ct);
    Task<UserAccount> CreateAsync(Guid actor, string name, string email, string password, UserRole role, CancellationToken ct);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct);
    Task ChangeAsync(Guid actor, Guid id, UserRole role, bool active, CancellationToken ct);
    Task PasswordAsync(Guid actor, Guid id, string password, CancellationToken ct);
}
