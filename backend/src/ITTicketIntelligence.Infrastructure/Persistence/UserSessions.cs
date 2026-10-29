using System.Security.Cryptography;
using System.Text;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
namespace ITTicketIntelligence.Infrastructure.Persistence;
internal sealed class UserSessions(AppDbContext db) : IUserSessions
{
    private const int Iterations = 310000;
    public async Task PasswordAsync(Guid actor,Guid id,string password,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(password) || password.Length is <14 or >256)throw new ITTicketIntelligence.Application.Common.RequestValidationException(new Dictionary<string,string[]>{["password"]=["Use 14 to 256 characters."]});
        var user=await db.Users.SingleOrDefaultAsync(u=>u.Id==id,ct)??throw new ITTicketIntelligence.Application.Common.ResourceNotFoundException("User");
        db.Entry(user).Property<string?>("PasswordHash").CurrentValue=Hash(password);db.Entry(user).Property<Guid>("AuthVersion").CurrentValue=Guid.NewGuid();db.Entry(user).Property<int>("LoginFailures").CurrentValue=0;db.Entry(user).Property<DateTimeOffset?>("LockoutUntil").CurrentValue=null;
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(),actor,"Admin.User.PasswordReset","User",id,DateTimeOffset.UtcNow));await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct) => await db.Users.AsNoTracking().OrderBy(u => u.Name).ThenBy(u => u.Id).Take(100).Select(u => new UserAccount(u.Id, u.Name, u.Email.Value, u.Role, u.IsActive)).ToListAsync(ct);
    public async Task<UserAccount> CreateAsync(Guid actor, string name, string email, string password, UserRole role, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 14 or > 256) throw new ITTicketIntelligence.Application.Common.RequestValidationException(new Dictionary<string,string[]> { ["password"]=["Use 14 to 256 characters."] });
        var user = new User(Guid.NewGuid(), name, EmailAddress.Create(email), role, DateTimeOffset.UtcNow); db.Users.Add(user);
        db.Entry(user).Property<string?>("PasswordHash").CurrentValue = Hash(password);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), actor, "Admin.User.Created", "User", user.Id, DateTimeOffset.UtcNow)); await db.SaveChangesAsync(ct);
        return new(user.Id, user.Name, user.Email.Value, user.Role, user.IsActive);
    }
    public async Task ChangeAsync(Guid actor, Guid id, UserRole role, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(18347293)",ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id,ct) ?? throw new ITTicketIntelligence.Application.Common.ResourceNotFoundException("User");
        if(user.Role == UserRole.Admin && user.IsActive && (role != UserRole.Admin || !active) && await db.Users.CountAsync(u=>u.Role==UserRole.Admin && u.IsActive,ct)<=1) throw new ITTicketIntelligence.Application.Common.ConflictException("Keep at least one active administrator.");
        var now=DateTimeOffset.UtcNow; user.ChangeRole(role,now); if(active)user.Reactivate(now);else user.Deactivate(now);
        db.Entry(user).Property<Guid>("AuthVersion").CurrentValue=Guid.NewGuid();
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(),actor,"Admin.User.AccessChanged","User",id,now));
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static string Hash(string password) { var salt = RandomNumberGenerator.GetBytes(32); return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32)); }
    private static bool Verify(string password, string hash) { try { var parts = hash.Split('.'); if(parts.Length != 2) return false; return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]), Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), Iterations, HashAlgorithmName.SHA256, 32)); } catch (FormatException) { return false; } }
    public async Task<SessionUser?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == id && u.IsActive).Select(u => new { u.Id, u.Name, Email = u.Email, u.Role, Hash = EF.Property<string?>(u, "PasswordHash"), Version = EF.Property<Guid>(u, "AuthVersion") }).SingleOrDefaultAsync(ct);
        if (row?.Hash is null) return null;
        var teams = db.TeamMembers.Where(m => m.UserId == id && db.Teams.Any(t => t.Id == m.TeamId && t.IsActive)).Select(m => m.TeamId);
        var ids = row.Role is UserRole.Admin or UserRole.Support ? Array.Empty<Guid>() : await db.Tickets.Where(t => row.Role == UserRole.User ? t.CreatedById == id : t.AssignedUserId == id || t.AssignedTeamId.HasValue && teams.Contains(t.AssignedTeamId.Value)).Select(t => t.Id).ToArrayAsync(ct);
        return new(row.Id, row.Name, row.Email.Value, row.Role, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Hash + row.Version))), ids);
    }
    public async Task<SessionUser?> LoginAsync(string email, string password, CancellationToken ct)
    {
        if (email.Length > 254 || password.Length > 256) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM app.users WHERE email = {email.Trim().ToLowerInvariant()} FOR UPDATE").SingleOrDefaultAsync(ct);
        var hash = user is null ? null : db.Entry(user).Property<string?>("PasswordHash").CurrentValue;
        var locked = user is not null && db.Entry(user).Property<DateTimeOffset?>("LockoutUntil").CurrentValue > DateTimeOffset.UtcNow;
        var valid = Verify(password, hash ?? "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=") && user?.IsActive == true && !locked;
        if (user is not null)
        {
            var failures = db.Entry(user).Property<int>("LoginFailures"); failures.CurrentValue = valid ? 0 : Math.Min(10, failures.CurrentValue + 1);
            if (!valid && failures.CurrentValue >= 5) db.Entry(user).Property<DateTimeOffset?>("LockoutUntil").CurrentValue = DateTimeOffset.UtcNow.AddMinutes(15);
            if (valid) db.Entry(user).Property<DateTimeOffset?>("LockoutUntil").CurrentValue = null;
        }
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), user?.Id, valid ? "Auth.Login.Succeeded" : "Auth.Login.Denied", "Authentication", user?.Id ?? Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return valid ? await FindAsync(user!.Id, ct) : null;
    }
    public async Task AuditAsync(Guid? userId, string action, CancellationToken ct) { db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, action, "Authentication", userId ?? Guid.NewGuid(), DateTimeOffset.UtcNow)); await db.SaveChangesAsync(ct); }
    public async Task BootstrapAsync(string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 14 or > 256) throw new InvalidOperationException("Bootstrap password must contain 14 to 256 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(18347293)", ct);
        if (await db.Users.AnyAsync(u => EF.Property<string?>(u, "PasswordHash") != null, ct)) return;
        var normalized = EmailAddress.Create(email);
        var user = await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM app.users WHERE email = {normalized.Value}").SingleOrDefaultAsync(ct);
        if(user is null){user=new User(Guid.NewGuid(),"Administrator",normalized,UserRole.Admin,DateTimeOffset.UtcNow);db.Users.Add(user);}
        else {user.ChangeRole(UserRole.Admin,DateTimeOffset.UtcNow);user.Reactivate(DateTimeOffset.UtcNow);}
        db.Entry(user).Property<string?>("PasswordHash").CurrentValue = Hash(password);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, "Auth.Admin.Bootstrapped", "User", user.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == id, ct);
        db.Entry(user).Property<Guid>("AuthVersion").CurrentValue = Guid.NewGuid();
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), id, "Auth.Tokens.Revoked", "User", id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}
