using System.Net.Http.Json;
using System.Text.Json;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.Extensions.Logging;

namespace ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;

/// <summary>Owns its dedicated HTTP client. Only tags/embed endpoints are used.</summary>
public sealed class OllamaEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient client;
    private readonly OllamaEmbeddingOptions options;
    private readonly ILogger<OllamaEmbeddingService> logger;
    public OllamaEmbeddingService(HttpClient client, OllamaEmbeddingOptions options, ILogger<OllamaEmbeddingService> logger)
    {
        Profile = options.ValidateAndGetProfile();
        this.client = client; this.options = options; this.logger = logger;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
    public EmbeddingProfile Profile { get; }

    public async Task<IReadOnlyList<TextEmbedding>> GenerateAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (texts.Count is < 1 or > 16 || texts.Any(string.IsNullOrWhiteSpace) || !Enum.IsDefined(purpose))
            throw new ArgumentException("Embedding requests require 1 to 16 nonempty inputs and a valid purpose.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var tagsResponse = await client.GetAsync("api/tags", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!tagsResponse.IsSuccessStatusCode) throw new RagUnavailableException("The embedding provider's model registry is unavailable.");
            var tags = await tagsResponse.Content.ReadFromJsonAsync<TagsResponse>(deadline.Token);
            if (tags?.Models is null || !tags.Models.Any(m => m.Name == options.Model && m.Digest == options.ModelDigest))
                throw new RagUnavailableException("The configured embedding model is missing or its digest changed. Install the pinned model and reindex when changing profiles.");
            var prefix = purpose == EmbeddingPurpose.Document ? options.DocumentPrefix : options.QueryPrefix;
            using var response = await client.PostAsJsonAsync("api/embed", new
            { model = options.Model, input = texts.Select(t => prefix + t).ToArray(), truncate = false }, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new RagUnavailableException($"The embedding provider failed with HTTP {(int)response.StatusCode}.");
            var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(deadline.Token);
            if (body?.Model != options.Model || body.Embeddings is null || body.Embeddings.Length != texts.Count || body.Embeddings.Any(v => v is null))
                throw new RagUnavailableException("The embedding provider returned an invalid response.");
            var result = body.Embeddings.Select(v => new TextEmbedding(Profile.Id,
                EmbeddingValidation.Normalize(new TextEmbedding(Profile.Id, v), Profile))).ToArray();
            logger.LogDebug("Embedding provider produced {Count} {Purpose} vectors, profile {Profile}", result.Length, purpose, Profile.Id);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning("Embedding provider failed: {FailureType}", ex.GetType().Name);
            throw new RagUnavailableException("The embedding provider is unavailable or returned invalid data.", ex);
        }
    }

    public void Dispose() => client.Dispose();
    private sealed record TagsResponse(ModelTag[]? Models);
    private sealed record ModelTag(string? Name, string? Digest);
    private sealed record EmbedResponse(string? Model, float[][]? Embeddings);
}
