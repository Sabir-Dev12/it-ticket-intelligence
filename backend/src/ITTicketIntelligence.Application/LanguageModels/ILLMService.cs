using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge.Retrieval;

namespace ITTicketIntelligence.Application.LanguageModels;

public interface ILLMService
{
    Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken cancellationToken = default);
}

public sealed class LLMRequest
{
    public const int MaximumPromptCharacters = 8000;
    public const int MaximumContextCharacters = 24000;

    public LLMRequest(string prompt, RetrievedContext? context = null)
    {
        var chunks = context?.Results?.ToArray() ?? [];
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaximumPromptCharacters || prompt.Contains('\0'))
            errors["prompt"] = ["Prompt obligatoire sans caractère NUL, maximum 8000 caractères."];
        if (chunks.Length > 20 || chunks.Any(c => c is null || c.Metadata is null || c.ChunkId == Guid.Empty ||
            c.DocumentId == Guid.Empty || c.Metadata.DocumentId != c.DocumentId || c.Metadata.DocumentVersion < 1 ||
            string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 160 || string.IsNullOrWhiteSpace(c.Chunk) ||
            c.Chunk.Length > 4000 || string.IsNullOrWhiteSpace(c.Metadata.Source) || c.Metadata.Source.Length > 512 ||
            c.Metadata.ContentHash is null || c.Metadata.ContentHash.Length != 64 || c.Metadata.ContentHash.Any(ch => !char.IsAsciiHexDigitLower(ch))))
            errors["context"] = ["Le contexte doit contenir au plus 20 chunks avec une provenance valide."];
        else if (chunks.Sum(c => c.Chunk.Length + c.Title.Length + c.Metadata.Source.Length) > MaximumContextCharacters ||
            chunks.Select(c => c.ChunkId).Distinct().Count() != chunks.Length)
            errors["context"] = ["Contexte limité à 24000 caractères avec des identifiants de chunks uniques."];
        if (errors.Count != 0) throw new RequestValidationException(errors);
        Prompt = prompt.Trim();
        Context = Array.AsReadOnly(chunks);
    }

    public string Prompt { get; }
    public IReadOnlyList<RetrievedChunk> Context { get; }
}

public sealed record LLMAnswer(string Summary, IReadOnlyList<string> SuggestedSteps, bool InsufficientContext);
public sealed record LLMCitation(Guid DocumentId, Guid ChunkId, int DocumentVersion, string Title, string Source, string ContentHash);
public sealed record LLMResponse(LLMAnswer Answer, IReadOnlyList<LLMCitation> Citations, string Provider, string Model,
    int Attempts, bool NeedsHumanReview);

public enum LLMFailureKind { Unavailable, Timeout, RateLimited, Authentication, ProviderRejected, InvalidResponse, Refused }

public sealed class LLMServiceException(LLMFailureKind kind) : Exception(kind switch
{
    LLMFailureKind.Timeout => "The language model request exceeded its time budget.",
    LLMFailureKind.RateLimited => "The language model provider is rate limited.",
    LLMFailureKind.Authentication => "The language model provider rejected authentication.",
    LLMFailureKind.ProviderRejected => "The language model provider rejected the request.",
    LLMFailureKind.InvalidResponse => "The language model returned an invalid or incomplete structured response.",
    LLMFailureKind.Refused => "The language model provider declined the request.",
    _ => "The language model provider is unavailable."
})
{
    public LLMFailureKind Kind { get; } = kind;
}
