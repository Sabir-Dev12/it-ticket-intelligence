using System.Security.Claims;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ITTicketIntelligence.Api.Realtime;

[Authorize(Policy = "TicketRealtime")]
public sealed class TicketHub(IQueryHandler<GetTicketQuery, TicketDto> tickets, ILogger<TicketHub> logger, Security.LiveConnections connections, ITTicketIntelligence.Application.Security.IUserSessions sessions) : Hub
{
    public const string Route = "/hubs/tickets";
    public const string AllGroup = "tickets:all";
    public static string Group(Guid ticketId) => "ticket:" + ticketId.ToString("N");

    public override async Task OnConnectedAsync()
    {
        connections.Clients[Context.ConnectionId] = Context.User!;
        if (Caller().AllTickets) await Groups.AddToGroupAsync(Context.ConnectionId, AllGroup, Context.ConnectionAborted);
        logger.LogInformation("Ticket hub connected {ConnectionId}, subject {Subject}", Context.ConnectionId, Context.User?.FindFirstValue(ClaimTypes.NameIdentifier));
        await base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        logger.LogInformation("Ticket hub disconnected {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    public async Task<TicketDto> JoinTicket(Guid ticketId)
    {
        await AuthorizeCurrent(ticketId);
        // Join before the snapshot read so an intervening update cannot fall into a subscription gap.
        if (!Caller().AllTickets) await Groups.AddToGroupAsync(Context.ConnectionId, Group(ticketId), Context.ConnectionAborted);
        connections.Subscribe(Context.ConnectionId, ticketId);
        try
        {
            var snapshot = await Read(ticketId);
            logger.LogInformation("Ticket hub joined {TicketId}, connection {ConnectionId}", ticketId, Context.ConnectionId);
            return snapshot;
        }
        catch
        {
            connections.Unsubscribe(Context.ConnectionId, ticketId);
            if (!Caller().AllTickets) await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(ticketId));
            throw;
        }
    }
    public async Task LeaveTicket(Guid ticketId)
    {
        await AuthorizeCurrent(ticketId);
        connections.Unsubscribe(Context.ConnectionId, ticketId);
        if (!Caller().AllTickets) await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(ticketId), Context.ConnectionAborted);
    }
    public async Task<TicketDto> GetTicket(Guid ticketId) { await AuthorizeCurrent(ticketId); return await Read(ticketId); }
    private async Task AuthorizeCurrent(Guid id)
    {
        if (!await Security.LiveConnections.CanRead(Context.User!, id, sessions, Context.ConnectionAborted)) throw new HubException("Access denied.");
    }
    private async Task<TicketDto> Read(Guid id)
    {
        try { return await tickets.HandleAsync(new(id), Context.ConnectionAborted); }
        catch (ResourceNotFoundException) { throw new HubException("Ticket introuvable."); }
    }
    private McpCaller Caller() => McpSecurityOptions.Caller(Context.User);
}
