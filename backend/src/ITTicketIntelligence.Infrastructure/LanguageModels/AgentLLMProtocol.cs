using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Mcp.Contracts;

namespace ITTicketIntelligence.Infrastructure.LanguageModels;

internal static class AgentLLMProtocol
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"properties":{
         "summary":{"type":"string","minLength":1,"maxLength":2000},"diagnosis":{"type":"string","minLength":1,"maxLength":3000},
         "possibleCauses":{"type":"array","maxItems":8,"items":{"type":"string","minLength":1,"maxLength":500}},
         "recommendedActions":{"type":"array","maxItems":10,"items":{"type":"string","minLength":1,"maxLength":800}},
         "citedChunkIds":{"type":"array","maxItems":20,"items":{"type":"string"}},
         "confidence":{"type":"number","minimum":0,"maximum":1},"insufficientContext":{"type":"boolean"}},
         "required":["summary","diagnosis","possibleCauses","recommendedActions","citedChunkIds","confidence","insufficientContext"]}
        """).RootElement.Clone();

    public static byte[] BuildBody(AgentLLMRequest request, LLMOptions options)
    {
        if (request.Messages is null || request.Messages.Count is < 2 or > 32 ||
            request.Messages.Sum(m => (long)m.Content.Length) > TicketAgent.MaximumTranscriptCharacters)
            throw Invalid();
        var ollama = options.Provider == LLMProvider.Ollama;
        var messages = request.Messages.Select(m => Message(m, ollama)).ToArray();
        var body = new Dictionary<string, object> { ["model"] = options.Model, ["messages"] = messages, ["stream"] = false };
        if (ollama)
        {
            body["think"] = false;
            body["options"] = new { temperature = 0, num_predict = options.MaxOutputTokens, num_ctx = options.ContextWindowTokens };
            if (request.Finalize) body["format"] = Schema;
        }
        else
        {
            body[options.TokenLimitParameter] = options.MaxOutputTokens;
            if (request.Finalize) body["response_format"] = new { type = "json_schema", json_schema = new { name = "ticket_agent_analysis", strict = true, schema = Schema } };
        }
        if (!request.Finalize)
        {
            body["tools"] = McpToolCatalog.All.Select(t => Tool(t, ollama)).ToArray();
            if (!ollama) { body["tool_choice"] = "auto"; body["parallel_tool_calls"] = false; }
        }
        return JsonSerializer.SerializeToUtf8Bytes(body, LLMProtocolData.Json);
    }

    private static object Tool(McpToolDefinition tool, bool ollama)
    {
        var function = new Dictionary<string, object>
        { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.InputSchema };
        // Optional pagination fields are intentionally optional; the application validates every call.
        if (!ollama) function["strict"] = false;
        return new { type = "function", function };
    }

    private static object Message(AgentMessage message, bool ollama)
    {
        if (message.Role is not ("system" or "user" or "assistant" or "tool")) throw Invalid();
        var data = new Dictionary<string, object?> { ["role"] = message.Role, ["content"] = message.Content };
        if (message.ToolCalls is { Count: > 0 }) data["tool_calls"] = message.ToolCalls.Select(c => ollama
            ? (object)new { type = "function", function = new { name = c.Name, arguments = JsonSerializer.Deserialize<JsonElement>(c.ArgumentsJson) } }
            : new { id = c.Id, type = "function", function = new { name = c.Name, arguments = c.ArgumentsJson } }).ToArray();
        if (message.Role == "tool") data[ollama ? "tool_name" : "tool_call_id"] = ollama ? message.ToolName : message.ToolCallId;
        return data;
    }

    public static AgentLLMTurn Read(JsonElement root, bool finalize, LLMProvider provider, int attempts)
    {
        JsonElement message; string? finish;
        var ollama = provider == LLMProvider.Ollama;
        if (ollama)
        {
            if (!root.GetProperty("done").GetBoolean()) throw Invalid();
            finish = root.GetProperty("done_reason").GetString(); message = root.GetProperty("message");
            if (finish != "stop") throw Invalid();
        }
        else
        {
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() != 1) throw Invalid();
            finish = choices[0].GetProperty("finish_reason").GetString(); message = choices[0].GetProperty("message");
            if (finish == "content_filter") throw new LLMServiceException(LLMFailureKind.Refused);
            if (finish != "stop" && (finalize || finish != "tool_calls")) throw Invalid();
        }
        var model = root.GetProperty("model").GetString();
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256 || model.Any(char.IsControl) || message.GetProperty("role").GetString() != "assistant") throw Invalid();
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(refusal.GetString()))
            throw new LLMServiceException(LLMFailureKind.Refused);
        if (message.TryGetProperty("function_call", out var legacy) && legacy.ValueKind != JsonValueKind.Null) throw Invalid();
        var calls = new List<AgentToolCall>();
        if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind != JsonValueKind.Null)
        {
            if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > TicketAgent.MaximumToolCalls || (finalize && tools.GetArrayLength() != 0)) throw Invalid();
            foreach (var call in tools.EnumerateArray())
            {
                if (call.TryGetProperty("type", out var type) && type.GetString() != "function") throw Invalid();
                var fn = call.GetProperty("function");
                var name = fn.GetProperty("name").GetString();
                var id = ollama ? Guid.NewGuid().ToString("N") : call.GetProperty("id").GetString();
                var args = fn.GetProperty("arguments");
                var arguments = ollama ? args.GetRawText() : args.GetString();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl) ||
                    string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl) || arguments is null || arguments.Length > 2000) throw Invalid();
                calls.Add(new(id, name, arguments));
            }
        }
        if (!ollama && ((finish == "tool_calls") != (calls.Count != 0))) throw Invalid();
        AgentDraft? draft = null;
        if (finalize)
        {
            var content = message.GetProperty("content").GetString() ?? throw Invalid();
            using var parsed = JsonDocument.Parse(content);
            var fields = parsed.RootElement;
            if (fields.ValueKind != JsonValueKind.Object || fields.EnumerateObject().Select(p => p.Name).Distinct().Count() != fields.EnumerateObject().Count()) throw Invalid();
            var body = JsonSerializer.Deserialize<DraftBody>(content, Strict) ?? throw Invalid();
            draft = new(body.Summary, body.Diagnosis, body.PossibleCauses, body.RecommendedActions, body.CitedChunkIds, body.Confidence, body.InsufficientContext);
        }
        return new(calls.AsReadOnly(), draft, provider.ToString(), model, attempts);
    }
    private static LLMServiceException Invalid() => new(LLMFailureKind.InvalidResponse);
    private sealed class DraftBody
    {
        public required string Summary { get; init; }
        public required string Diagnosis { get; init; }
        public required string[] PossibleCauses { get; init; }
        public required string[] RecommendedActions { get; init; }
        public required string[] CitedChunkIds { get; init; }
        public required double Confidence { get; init; }
        public required bool InsufficientContext { get; init; }
    }
}
