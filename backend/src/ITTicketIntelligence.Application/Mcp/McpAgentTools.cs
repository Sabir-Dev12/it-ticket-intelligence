using System.Text.Json;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Mcp.Contracts;

namespace ITTicketIntelligence.Application.Mcp;

/// <summary>In-process MCP tool client. Host-bound identity and the same application policies as the HTTP server.</summary>
public sealed class McpAgentTools(IMcpToolService service) : ITicketAgentTools
{
    public async Task<AgentToolResult> ExecuteAsync(Guid ticketId, AgentToolCall call, CancellationToken cancellationToken)
    {
        JsonElement arguments;
        try { using var doc = JsonDocument.Parse(call.ArgumentsJson); arguments = doc.RootElement.Clone(); }
        catch (JsonException) { throw new AgentToolDeniedException(); }
        if (arguments.ValueKind != JsonValueKind.Object) throw new AgentToolDeniedException();
        var name = call.Name == "get_team_information" ? "get_team" : call.Name; // Compatibility with earlier agent transcripts.
        if (name is "get_ticket" or "get_ticket_history" && !arguments.EnumerateObject().Any())
            arguments = JsonSerializer.SerializeToElement(new { ticketId });
        var result = await service.CallAsync(name, arguments, McpCaller.ForAgent(ticketId), cancellationToken);
        if (result.IsError)
        {
            var code = result.Content.GetProperty("error").GetProperty("code").GetString();
            if (code == "NotFound") throw new ResourceNotFoundException("Ticket");
            if (code is "Forbidden" or "UnknownTool" or "InvalidArguments") throw new AgentToolDeniedException();
            throw new McpUnavailableException();
        }
        if (name == "get_ticket")
        {
            var value = result.Content.Deserialize<McpTicketOutput>(McpToolCatalog.Json)!;
            return new(JsonSerializer.Serialize(value.Ticket, McpToolCatalog.Json), Ticket: value.Ticket);
        }
        if (name == "search_knowledge")
        {
            var value = result.Content.Deserialize<McpKnowledgeOutput>(McpToolCatalog.Json)!;
            return new(JsonSerializer.Serialize(value.Results, McpToolCatalog.Json), new RetrievedContext("mcp", value.Results));
        }
        return new(result.Content.GetRawText());
    }
}
