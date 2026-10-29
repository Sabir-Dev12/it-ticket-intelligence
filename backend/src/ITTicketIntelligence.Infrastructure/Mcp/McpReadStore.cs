using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Mcp;

internal sealed class McpReadStore(AppDbContext db, ITicketAgentData tickets, TimeProvider clock) : IMcpReadStore
{
    public Task<AgentTicketFacts?> GetTicketAsync(Guid id, CancellationToken ct) => tickets.GetTicketAsync(id, ct);
    public Task<IReadOnlyList<AgentHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken ct) => tickets.GetHistoryAsync(id, ct);
    public Task<AgentTeamInformation?> GetTeamAsync(string name, CancellationToken ct) => tickets.GetTeamAsync(name, ct);
    public async Task<McpSearchOutput> SearchAsync(string query, int page, int pageSize, McpCaller scope, CancellationToken ct)
    {
        var rows = db.Tickets.AsNoTracking().Where(t => !t.IsArchived);
        if (!scope.AllTickets) { var ids = scope.TicketIds.ToArray(); rows = rows.Where(t => ids.Contains(t.Id)); }
        if (query.Length != 0) rows = rows.Where(t => t.Title.Contains(query) || t.Description.Contains(query));
        var result = await rows.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(t => new AgentTicketFacts(t.Id, t.Revision, t.Title, t.Description, t.Status, t.IsArchived, t.AssignedTeamId)).ToListAsync(ct);
        return new(result.Take(pageSize).ToArray(), page, pageSize, result.Count > pageSize);
    }
    public async Task<McpStatusOutput> GetStatusAsync(CancellationToken ct)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct); probe.CancelAfter(TimeSpan.FromSeconds(3));
        bool available;
        try { available = await db.Database.CanConnectAsync(probe.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { available = false; }
        ct.ThrowIfCancellationRequested();
        return new("ITTicketIntelligence", available ? "Available" : "Unavailable", clock.GetUtcNow(), true);
    }
}
