using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Knowledge.Retrieval;

namespace ITTicketIntelligence.Application.Agents;

public sealed class TicketAgentTools(ITicketAgentData data, IRagRetriever rag) : ITicketAgentTools
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public async Task<AgentToolResult> ExecuteAsync(Guid ticketId, AgentToolCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonDocument args;
        try { args = JsonDocument.Parse(call.ArgumentsJson); }
        catch (JsonException) { throw new AgentToolDeniedException(); }
        using (args)
        {
            var root = args.RootElement;
            if (root.ValueKind != JsonValueKind.Object || call.ArgumentsJson.Length > 2000)
                throw new AgentToolDeniedException();
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct().Count() != properties.Length) throw new AgentToolDeniedException();
            switch (call.Name)
            {
                case "get_ticket" when properties.Length == 0:
                    var ticket = await data.GetTicketAsync(ticketId, cancellationToken) ?? throw new ResourceNotFoundException("Ticket");
                    return new(JsonSerializer.Serialize(ticket, Json), Ticket: ticket);
                case "get_ticket_history" when properties.Length == 0:
                    return new(JsonSerializer.Serialize(await data.GetHistoryAsync(ticketId, cancellationToken), Json));
                case "search_knowledge" when properties.Length == 1 && properties[0].Name == "query":
                    var query = Text(properties[0].Value, 500);
                    var context = await rag.RetrieveAsync(new RagQuery(query, Limit: 3), cancellationToken);
                    // Embedding arrays and internal profile configuration are never sent to the model.
                    return new(JsonSerializer.Serialize(context.Results.Select(c => new { c.ChunkId, c.DocumentId,
                        c.Title, text = c.Chunk, c.Metadata.DocumentVersion, c.Metadata.Source, c.RelevanceScore }), Json), context);
                case "get_team_information" when properties.Length == 1 && properties[0].Name == "name":
                    var name = Text(properties[0].Value, 100);
                    if (!TicketTeamNames.All.Contains(name, StringComparer.Ordinal)) throw new AgentToolDeniedException();
                    return new(JsonSerializer.Serialize(await data.GetTeamAsync(name, cancellationToken), Json));
                default: throw new AgentToolDeniedException();
            }
        }
    }

    private static string Text(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) throw new AgentToolDeniedException();
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)) throw new AgentToolDeniedException();
        return text.Trim();
    }
}
