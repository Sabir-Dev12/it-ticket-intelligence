using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Mcp.Contracts;

namespace ITTicketIntelligence.Application.Mcp;

public sealed class McpToolService(IMcpReadStore data, IRagRetriever rag, IMcpAuditWriter audit, TimeProvider clock) : IMcpToolService
{
    public IReadOnlyList<McpToolDefinition> List(McpCaller caller) => McpToolCatalog.All.Where(t => caller.Permissions.Contains(t.Permission)).ToArray();

    public async Task<McpToolResponse> CallAsync(string name, JsonElement arguments, McpCaller caller, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        var token = budget.Token;
        var callId = Guid.NewGuid();
        var tool = McpToolCatalog.All.SingleOrDefault(t => t.Name == name);
        var safeName = tool?.Name ?? "unrecognized";
        var argsHash = Hash(arguments.ValueKind == JsonValueKind.Undefined ? "undefined" : arguments.GetRawText());
        Task Audit(string outcome, string? output, CancellationToken ct) => audit.WriteAsync(new(callId, caller.Subject,
            safeName, outcome, argsHash, output is null ? null : Hash(output), clock.GetUtcNow()), ct);
        // Fail closed: no data service runs until the initial audit is durably written.
        await Audit("Started", null, token);
        var outcome = "Failed";
        try
        {
            if (tool is null) { outcome = "Denied"; return await Error("UnknownTool", "Unknown controlled tool."); }
            if (!caller.Permissions.Contains(tool.Permission)) { outcome = "Denied"; return await Error("Forbidden", "This tool is not permitted."); }
            try { McpToolCatalog.Validate(tool, arguments); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
            { outcome = "Rejected"; return await Error("InvalidArguments", "Arguments do not match the tool input schema."); }
            Guid? id = arguments.TryGetProperty("ticketId", out var ticketId) ? ticketId.GetGuid() : null;
            if (id.HasValue && !caller.CanRead(id.Value)) { outcome = "Denied"; return await Error("Forbidden", "The requested ticket is outside the authorized scope."); }
            object result = name switch
            {
                "get_ticket" => new McpTicketOutput(await data.GetTicketAsync(id!.Value, token) ?? throw new ResourceNotFoundException("Ticket")),
                "search_tickets" => await data.SearchAsync(arguments.GetProperty("query").GetString()!.Trim(), Number("page", 1), Number("pageSize", 10), caller, token),
                "get_ticket_history" => await History(id!.Value),
                "search_knowledge" => new McpKnowledgeOutput((await rag.RetrieveAsync(new RagQuery(arguments.GetProperty("query").GetString()!.Trim(), Number("limit", 3)), token)).Results),
                "get_team" => new McpTeamOutput(await data.GetTeamAsync(arguments.GetProperty("name").GetString()!, token)),
                "get_system_status" => await data.GetStatusAsync(token),
                _ => throw new InvalidOperationException()
            };
            var content = JsonSerializer.SerializeToElement(result, result.GetType(), McpToolCatalog.Json);
            if (content.GetRawText().Length > 64000) throw new McpUnavailableException();
            token.ThrowIfCancellationRequested();
            await Audit("Succeeded", content.GetRawText(), token);
            return new(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Audit("Cancelled", null, cleanup.Token); throw;
        }
        catch (ResourceNotFoundException) { return await Error("NotFound", "The requested resource does not exist."); }
        catch
        {
            // Raw DB, provider and argument exceptions never become tool content or logs.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Audit("Failed", null, cleanup.Token);
            return Failure("Unavailable", "The controlled tool could not complete.");
        }

        int Number(string key, int fallback) => arguments.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
        async Task<McpHistoryOutput> History(Guid value)
        {
            _ = await data.GetTicketAsync(value, token) ?? throw new ResourceNotFoundException("Ticket");
            return new(value, await data.GetHistoryAsync(value, token));
        }
        async Task<McpToolResponse> Error(string code, string message) { await Audit(outcome, null, token); return Failure(code, message); }
        McpToolResponse Failure(string code, string message) => new(JsonSerializer.SerializeToElement(new { error = new McpError(code, message, callId) }, McpToolCatalog.Json), true);
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
