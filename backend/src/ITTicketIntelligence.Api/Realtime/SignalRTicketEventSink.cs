using ITTicketIntelligence.Application.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace ITTicketIntelligence.Api.Realtime;

public sealed class SignalRTicketEventSink(IHubContext<TicketHub> hub, Security.LiveConnections connections, IServiceScopeFactory scopes) : ITicketEventSink
{
    public async Task SendAsync(TicketEvent notification, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope(); var sessions = scope.ServiceProvider.GetRequiredService<ITTicketIntelligence.Application.Security.IUserSessions>();
        foreach (var entry in connections.Clients)
            if ((Mcp.McpSecurityOptions.Caller(entry.Value).AllTickets || connections.IsSubscribed(entry.Key, notification.TicketId)) && await Security.LiveConnections.CanRead(entry.Value, notification.TicketId, sessions, cancellationToken))
                await hub.Clients.Client(entry.Key).SendCoreAsync(notification.Name.ToString(), [notification], cancellationToken);
    }
}
