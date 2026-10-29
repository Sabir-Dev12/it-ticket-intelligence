using System.Security.Cryptography;
using System.Text;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;

public sealed class OllamaEmbeddingOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434/";
    public string Model { get; init; } = "nomic-embed-text:latest";
    public string ModelDigest { get; init; } = "0a109f422b47e3a30ba2b10eca18548e944e8a23073ee3f3e947efcf3c45e59f";
    public int Dimensions { get; init; } = 768;
    public int TimeoutSeconds { get; init; } = 30;
    public string DocumentPrefix { get; init; } = "search_document: ";
    public string QueryPrefix { get; init; } = "search_query: ";

    public EmbeddingProfile ValidateAndGetProfile()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || ModelDigest.Length != 64 ||
            ModelDigest.Any(c => !char.IsAsciiHexDigitLower(c)) || Dimensions is < 1 or > 4096 ||
            TimeoutSeconds is < 1 or > 60 || DocumentPrefix.Length > 200 || QueryPrefix.Length > 200)
            throw new InvalidOperationException("Rag:Ollama configuration is invalid. Configure a pinned model digest, dimension and HTTP endpoint.");
        var promptHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentPrefix + "\0" + QueryPrefix)))[..16];
        return new($"ollama:{Model}@{ModelDigest}:d{Dimensions}:title-chunk-v1:{promptHash}", Dimensions);
    }

    public static OllamaEmbeddingOptions FromConfiguration(IConfiguration configuration)
    {
        var c = configuration.GetSection("Rag:Ollama"); var defaults = new OllamaEmbeddingOptions();
        return new()
        {
            BaseUrl = c["BaseUrl"] ?? defaults.BaseUrl, Model = c["Model"] ?? defaults.Model,
            ModelDigest = c["ModelDigest"] ?? defaults.ModelDigest, Dimensions = int.Parse(c["Dimensions"] ?? "768", CultureInfo.InvariantCulture),
            TimeoutSeconds = int.Parse(c["TimeoutSeconds"] ?? "30", CultureInfo.InvariantCulture),
            DocumentPrefix = c["DocumentPrefix"] ?? defaults.DocumentPrefix, QueryPrefix = c["QueryPrefix"] ?? defaults.QueryPrefix
        };
    }
}
