using System.Collections.Concurrent;
using System.Security.Claims;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Api.Mcp;
namespace ITTicketIntelligence.Api.Security;
public sealed class LiveConnections
{
    public ConcurrentDictionary<string, ClaimsPrincipal> Clients { get; } = new();
    private readonly ConcurrentDictionary<(string Connection, Guid Ticket), byte> subscriptions = new();
    public void Subscribe(string connection, Guid ticket) => subscriptions.TryAdd((connection, ticket), 0);
    public void Unsubscribe(string connection, Guid ticket) => subscriptions.TryRemove((connection, ticket), out _);
    public bool IsSubscribed(string connection, Guid ticket) => subscriptions.ContainsKey((connection, ticket));
    public void Remove(string connection)
    {
        Clients.TryRemove(connection, out _);
        foreach (var key in subscriptions.Keys.Where(k => k.Connection == connection)) subscriptions.TryRemove(key, out _);
    }
    public static async Task<bool> CanRead(ClaimsPrincipal principal, Guid ticketId, IUserSessions sessions, CancellationToken ct)
    {
        if (principal.HasClaim(c => c.Type == "session.expires") && (!long.TryParse(principal.FindFirstValue("session.expires"), out var expires) || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())) return false;
        if (Guid.TryParse(principal.FindFirstValue("assistant.user"), out var id))
        {
            var user = await sessions.FindAsync(id, ct);
            return user is not null && (!principal.HasClaim(c => c.Type == "session.user") || user.Version == principal.FindFirstValue("session.version")) &&
                (user.Role is Domain.Enums.UserRole.Admin or Domain.Enums.UserRole.Support || user.TicketIds.Contains(ticketId));
        }
        return principal.Identity?.IsAuthenticated == true && McpSecurityOptions.Caller(principal).CanRead(ticketId);
    }
}
