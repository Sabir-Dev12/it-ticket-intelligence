using System.Collections.Frozen;
using System.Text.Json;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Knowledge.Retrieval;

namespace ITTicketIntelligence.Application.Mcp.Contracts;

public sealed record McpToolDefinition(string Name, string Description, string Permission, JsonElement InputSchema, JsonElement OutputSchema);
public sealed record McpTicketOutput(AgentTicketFacts Ticket);
public sealed record McpSearchOutput(IReadOnlyList<AgentTicketFacts> Items, int Page, int PageSize, bool HasMore);
public sealed record McpHistoryOutput(Guid TicketId, IReadOnlyList<AgentHistoryEntry> Entries);
public sealed record McpKnowledgeOutput(IReadOnlyList<RetrievedChunk> Results);
public sealed record McpTeamOutput(AgentTeamInformation? Team);
public sealed record McpStatusOutput(string Service, string Database, DateTimeOffset CheckedAt, bool ReadOnlyTools);
public sealed record McpError(string Code, string Message, Guid CallId);
public sealed record McpToolResponse(JsonElement Content, bool IsError = false);

/// <summary>Host-created authority, never deserialized from a tool's arguments.</summary>
public sealed class McpCaller
{
    public McpCaller(string subject, IEnumerable<string> permissions, IEnumerable<Guid>? ticketIds = null, bool allTickets = false)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 128 || subject.Any(char.IsControl)) throw new ArgumentException("Invalid MCP subject.");
        Subject = subject;
        Permissions = permissions.ToFrozenSet(StringComparer.Ordinal);
        if (Permissions.Any(p => !McpToolCatalog.All.Any(t => t.Permission == p))) throw new ArgumentException("Unknown MCP permission.");
        TicketIds = (ticketIds ?? []).ToFrozenSet();
        if (TicketIds.Count > 100 || TicketIds.Contains(Guid.Empty)) throw new ArgumentException("Invalid MCP ticket scope.");
        AllTickets = allTickets;
    }
    public string Subject { get; }
    public FrozenSet<string> Permissions { get; }
    public FrozenSet<Guid> TicketIds { get; }
    public bool AllTickets { get; }
    public bool CanRead(Guid ticketId) => AllTickets || TicketIds.Contains(ticketId);
    public static McpCaller ForAgent(Guid ticketId) => new("ticket-agent", McpToolCatalog.All.Select(t => t.Permission), [ticketId]);
}

public sealed record McpAuditEntry(Guid CallId, string Subject, string Tool, string Outcome, string ArgumentsHash,
    string? OutputHash, DateTimeOffset OccurredAt);

public interface IMcpToolService
{
    IReadOnlyList<McpToolDefinition> List(McpCaller caller);
    Task<McpToolResponse> CallAsync(string name, JsonElement arguments, McpCaller caller, CancellationToken cancellationToken = default);
}
public interface IMcpReadStore
{
    Task<AgentTicketFacts?> GetTicketAsync(Guid id, CancellationToken cancellationToken);
    Task<McpSearchOutput> SearchAsync(string query, int page, int pageSize, McpCaller scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken cancellationToken);
    Task<AgentTeamInformation?> GetTeamAsync(string name, CancellationToken cancellationToken);
    Task<McpStatusOutput> GetStatusAsync(CancellationToken cancellationToken);
}
public interface IMcpAuditWriter
{
    Task WriteAsync(McpAuditEntry entry, CancellationToken cancellationToken);
}
public sealed class McpUnavailableException : Exception
{
    public McpUnavailableException() : base("The controlled tool service is unavailable.") { }
}
