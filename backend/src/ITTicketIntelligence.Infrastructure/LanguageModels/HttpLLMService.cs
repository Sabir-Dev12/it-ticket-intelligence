using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Agents;
using Microsoft.Extensions.Logging;

namespace ITTicketIntelligence.Infrastructure.LanguageModels;

/// <summary>HTTP-only adapter. It owns its client and has no persistence or tool dependencies.</summary>
public sealed partial class HttpLLMService : ILLMService, ILLMAgentService, IAIChatService, IDisposable
{
    public const int MaximumResponseBytes = 256 * 1024;
    private readonly HttpClient client;
    private readonly LLMOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<HttpLLMService> logger;
    private readonly ILLMProtocol protocol;

    public HttpLLMService(HttpClient client, LLMOptions options, TimeProvider clock, ILogger<HttpLLMService> logger)
    {
        options.Validate();
        this.client = client; this.options = options; this.clock = clock; this.logger = logger;
        protocol = options.Provider == LLMProvider.Ollama ? new OllamaLLMProtocol() : new OpenAICompatibleLLMProtocol();
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(protocol.BuildBody(request, options), (root, attempts) =>
        {
            var parsed = protocol.ReadEnvelope(root);
            return StructuredLLMAnswer.Parse(parsed.Content, request, options.Provider.ToString(), parsed.Model, attempts);
        }, cancellationToken);
    }

    public Task<AgentLLMTurn> CompleteTurnAsync(AgentLLMRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(AgentLLMProtocol.BuildBody(request, options),
            (root, attempts) => AgentLLMProtocol.Read(root, request.Finalize, options.Provider, attempts), cancellationToken);
    }

    private async Task<T> RunAsync<T>(byte[] body, Func<JsonElement, int, T> parse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await chatSlots.WaitAsync(0,cancellationToken)) throw new LLMServiceException(LLMFailureKind.RateLimited);
        var watch = Stopwatch.StartNew();
        var attempts = 0;
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
        try
        {
            for (attempts = 1; attempts <= options.MaxRetries + 1; attempts++)
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
                try
                {
                    var answer = await SendAsync(body, parse, attempts, attempt.Token);
                    cancellationToken.ThrowIfCancellationRequested();
                    overall.Token.ThrowIfCancellationRequested();
                    logger.LogInformation("LLM completed with {Provider}, model {Model}, attempts {Attempts}, {ElapsedMs} ms",
                        options.Provider, options.Model, attempts, watch.ElapsedMilliseconds);
                    return answer;
                }
                catch (OperationCanceledException) when (overall.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { await RetryAsync(LLMFailureKind.Timeout, null, attempts, overall.Token); }
                catch (HttpRequestException) { await RetryAsync(LLMFailureKind.Unavailable, null, attempts, overall.Token); }
                catch (IOException) { await RetryAsync(LLMFailureKind.Unavailable, null, attempts, overall.Token); }
                catch (TransientFailure failure) { await RetryAsync(failure.Kind, failure.RetryAfter, attempts, overall.Token); }
            }
            throw new LLMServiceException(LLMFailureKind.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("LLM cancelled for {Provider}, attempts {Attempts}", options.Provider, attempts);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("LLM total timeout for {Provider}, attempts {Attempts}", options.Provider, attempts);
            throw new LLMServiceException(LLMFailureKind.Timeout);
        }
        catch (LLMServiceException failure)
        {
            // Never log raw exceptions, provider bodies, prompts, context or credentials.
            logger.LogWarning("LLM failed for {Provider}: {FailureKind}, attempts {Attempts}, {ElapsedMs} ms",
                options.Provider, failure.Kind, attempts, watch.ElapsedMilliseconds);
            throw;
        }
        finally { chatSlots.Release(); }
    }

    private async Task RetryAsync(LLMFailureKind kind, TimeSpan? retryAfter, int attempts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (attempts > options.MaxRetries) throw new LLMServiceException(kind);
        var delay = TimeSpan.FromMilliseconds(Math.Min(2000, options.RetryDelayMilliseconds * Math.Pow(2, attempts - 1)));
        if (retryAfter > delay) delay = retryAfter.Value;
        // Do not retry earlier than a long provider Retry-After just to fit our local budget.
        if (delay > TimeSpan.FromSeconds(10)) throw new LLMServiceException(kind);
        logger.LogWarning("LLM transient failure for {Provider}: {FailureKind}; retry {Retry} after {DelayMs} ms",
            options.Provider, kind, attempts, delay.TotalMilliseconds);
        await Task.Delay(delay, clock, token);
    }

    private async Task<T> SendAsync<T>(byte[] body, Func<JsonElement, int, T> parse, int attempts, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, protocol.Path);
        message.Content = new ByteArrayContent(body);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            var header = response.Headers.RetryAfter;
            var retryAfter = header?.Delta ?? (header?.Date is { } date ? date - clock.GetUtcNow() : (TimeSpan?)null);
            throw new TransientFailure(response.StatusCode == HttpStatusCode.TooManyRequests ? LLMFailureKind.RateLimited : LLMFailureKind.Unavailable, retryAfter);
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new LLMServiceException(LLMFailureKind.Authentication);
        if (!response.IsSuccessStatusCode) throw new LLMServiceException(LLMFailureKind.ProviderRejected);
        try
        {
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new LLMServiceException(LLMFailureKind.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream(); var buffer = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (bytes.Length + length > MaximumResponseBytes) throw new LLMServiceException(LLMFailureKind.InvalidResponse);
                await bytes.WriteAsync(buffer.AsMemory(0, length), token);
            }
            token.ThrowIfCancellationRequested();
            using var envelope = JsonDocument.Parse(bytes.ToArray());
            return parse(envelope.RootElement, attempts);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new LLMServiceException(LLMFailureKind.InvalidResponse); }
    }

    public void Dispose() { chatAdapter?.Dispose();client.Dispose();chatSlots.Dispose(); }
    private sealed class TransientFailure(LLMFailureKind kind, TimeSpan? retryAfter) : Exception
    { public LLMFailureKind Kind { get; } = kind; public TimeSpan? RetryAfter { get; } = retryAfter; }
}
