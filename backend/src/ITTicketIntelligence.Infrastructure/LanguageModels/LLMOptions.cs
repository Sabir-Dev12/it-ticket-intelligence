using System.Globalization;

namespace ITTicketIntelligence.Infrastructure.LanguageModels;

public enum LLMProvider { Ollama, OpenAICompatible }

// Intentionally not a record: a generated ToString must never expose the API key.
public sealed class LLMOptions
{
    public LLMProvider Provider { get; init; } = LLMProvider.Ollama;
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434/";
    public string Model { get; init; } = "qwen3:1.7b";
    [System.Text.Json.Serialization.JsonIgnore] public string? ApiKey { get; init; }
    public int AttemptTimeoutSeconds { get; init; } = 30;
    public int TotalTimeoutSeconds { get; init; } = 90;
    public int MaxRetries { get; init; } = 2;
    public int RetryDelayMilliseconds { get; init; } = 200;
    public int MaxOutputTokens { get; init; } = 1024;
    public int ContextWindowTokens { get; init; } = 16384;
    public string TokenLimitParameter { get; init; } = "max_completion_tokens";

    public void Validate()
    {
        if (!Enum.IsDefined(Provider) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme == "http" && !uri.IsLoopback) || string.IsNullOrWhiteSpace(Model) || Model.Length > 128 ||
            Model.Any(c => !char.IsAsciiLetterOrDigit(c) && !"._:/-".Contains(c)) ||
            AttemptTimeoutSeconds is < 1 or > 120 || TotalTimeoutSeconds < AttemptTimeoutSeconds || TotalTimeoutSeconds > 300 ||
            MaxRetries is < 0 or > 2 || RetryDelayMilliseconds is < 0 or > 2000 || MaxOutputTokens is < 16 or > 4096 ||
            ContextWindowTokens is < 2048 or > 32768 || TokenLimitParameter is not ("max_tokens" or "max_completion_tokens"))
            throw new InvalidOperationException("LLM environment configuration is invalid. Check provider, endpoint, model and bounded time/retry settings.");
        if ((Provider == LLMProvider.OpenAICompatible && string.IsNullOrWhiteSpace(ApiKey)) ||
            (ApiKey is not null && (ApiKey.Length > 4096 || ApiKey.Any(char.IsControl))))
            throw new InvalidOperationException("Configure LLM__ApiKey in the environment for the selected provider.");
    }

    public static LLMOptions FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var providerName = read("LLM__Provider") ?? "Ollama";
        var provider = providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ? LLMProvider.Ollama :
            providerName.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ? LLMProvider.OpenAICompatible :
            throw new InvalidOperationException("LLM__Provider must be Ollama or OpenAICompatible.");
        int Number(string key, int fallback) => read("LLM__" + key) is not { } raw ? fallback :
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value :
            throw new InvalidOperationException("LLM numeric environment configuration is invalid.");
        var model = read("LLM__Model") ?? (provider == LLMProvider.Ollama ? "qwen3:1.7b" : "");
        var options = new LLMOptions
        {
            Provider = provider, BaseUrl = read("LLM__BaseUrl") ?? (provider == LLMProvider.Ollama ? "http://127.0.0.1:11434/" : "https://api.openai.com/v1/"),
            Model = model, ApiKey = read("LLM__ApiKey"), AttemptTimeoutSeconds = Number("AttemptTimeoutSeconds", 30),
            TotalTimeoutSeconds = Number("TotalTimeoutSeconds", 90), MaxRetries = Number("MaxRetries", 2),
            RetryDelayMilliseconds = Number("RetryDelayMilliseconds", 200), MaxOutputTokens = Number("MaxOutputTokens", 1024),
            ContextWindowTokens = Number("ContextWindowTokens", 16384), TokenLimitParameter = read("LLM__TokenLimitParameter") ?? "max_completion_tokens"
        };
        options.Validate();
        return options;
    }
}
