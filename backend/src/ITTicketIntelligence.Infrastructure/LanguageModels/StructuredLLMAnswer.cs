using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.LanguageModels;

namespace ITTicketIntelligence.Infrastructure.LanguageModels;

internal static class StructuredLLMAnswer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false };

    internal static LLMResponse Parse(string content, LLMRequest request, string provider, string model, int attempts)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                json.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != json.RootElement.EnumerateObject().Count())
                throw new LLMServiceException(LLMFailureKind.InvalidResponse);
            var body = JsonSerializer.Deserialize<AnswerBody>(content, Json);
            if (body is null || string.IsNullOrWhiteSpace(body.Summary) || body.Summary.Length > 4000 ||
                body.SuggestedSteps is null || body.SuggestedSteps.Length > 10 ||
                body.SuggestedSteps.Any(step => string.IsNullOrWhiteSpace(step) || step.Length > 800) ||
                body.CitedChunkIds is null || body.CitedChunkIds.Length > 20 ||
                body.CitedChunkIds.Distinct().Count() != body.CitedChunkIds.Length ||
                (!body.InsufficientContext && (request.Context.Count == 0 || body.CitedChunkIds.Length == 0)))
                throw new LLMServiceException(LLMFailureKind.InvalidResponse);
            var context = request.Context.ToDictionary(chunk => chunk.ChunkId);
            var citations = new List<LLMCitation>();
            var citedIds = new HashSet<Guid>();
            foreach (var rawId in body.CitedChunkIds)
            {
                if (!Guid.TryParseExact(rawId, "D", out var id) || !citedIds.Add(id) || !context.TryGetValue(id, out var chunk))
                    throw new LLMServiceException(LLMFailureKind.InvalidResponse);
                citations.Add(new(chunk.DocumentId, chunk.ChunkId, chunk.Metadata.DocumentVersion, chunk.Title,
                    chunk.Metadata.Source, chunk.Metadata.ContentHash));
            }
            return new(new LLMAnswer(body.Summary.Trim(), Array.AsReadOnly(body.SuggestedSteps.Select(s => s.Trim()).ToArray()),
                body.InsufficientContext), citations.AsReadOnly(), provider, model, attempts, NeedsHumanReview: true);
        }
        catch (JsonException) { throw new LLMServiceException(LLMFailureKind.InvalidResponse); }
    }

    private sealed class AnswerBody
    {
        public required string Summary { get; init; }
        public required string[] SuggestedSteps { get; init; }
        public required string[] CitedChunkIds { get; init; }
        public required bool InsufficientContext { get; init; }
    }
}
