using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.LanguageModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace ITTicketIntelligence.Infrastructure.LanguageModels;

// Plain chat extends the existing provider, configuration and error contract.
public sealed partial class HttpLLMService
{
    private readonly SemaphoreSlim chatSlots = new(1, 1);
    private IChatClient? chatAdapter;

    private void ValidateChat(IReadOnlyList<AIChatMessage> messages, string? model)
    {
        if (options.Provider != LLMProvider.Ollama || !new Uri(options.BaseUrl).IsLoopback || options.Model.Contains(":cloud",StringComparison.OrdinalIgnoreCase))
            throw new LLMServiceException(LLMFailureKind.ProviderRejected);
        if ((model is not null && model != options.Model) || messages.Count is < 1 or > 21 ||
            messages.Sum(m=>m.Content?.Length ?? 0)>32000 || messages.Any(m=>m.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(m.Content) || m.Content.Length>16000 || m.Content.Contains('\0')))
            throw new RequestValidationException(new Dictionary<string,string[]> { ["chat"]=["Use the server-approved model and at most 21 user/assistant messages, 32000 characters total."] });
    }

    public async Task<string> SendAsync(IReadOnlyList<AIChatMessage> messages, string? model, CancellationToken ct)
    {
        var result = new StringBuilder();
        await foreach(var part in StreamAsync(messages,model,ct)) result.Append(part);
        return result.ToString();
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AIChatMessage> messages, string? model, [EnumeratorCancellation] CancellationToken ct)
    {
        ValidateChat(messages,model);
        // No unbounded queue on the local GPU. A second generation fails fast.
        if (!await chatSlots.WaitAsync(0,ct)) throw new LLMServiceException(LLMFailureKind.RateLimited);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
        try
        {
            IChatClient adapter = chatAdapter ??= new OllamaApiClient(client,options.Model);
            var history=messages.Select(m=>new ChatMessage(m.Role=="user"?ChatRole.User:ChatRole.Assistant,m.Content));
            var chatOptions=new ChatOptions { ModelId=options.Model, MaxOutputTokens=options.MaxOutputTokens, Temperature=0 };
            await using var stream=adapter.GetStreamingResponseAsync(history,chatOptions,timeout.Token).GetAsyncEnumerator(timeout.Token);
            var count=0;
            var completed=false;
            while(await NextAsync(stream,ct))
            {
                if(stream.Current.ModelId is { } returnedModel && returnedModel!=options.Model) throw new LLMServiceException(LLMFailureKind.InvalidResponse);
                if(stream.Current.FinishReason is { } reason) completed=reason==ChatFinishReason.Stop;
                var text=stream.Current.Text;
                if(text.Contains('\0'))throw new LLMServiceException(LLMFailureKind.InvalidResponse);
                count+=text.Length;
                if(count>16000) throw new LLMServiceException(LLMFailureKind.InvalidResponse);
                if(text.Length>0) yield return text;
            }
            if(count==0 || !completed) throw new LLMServiceException(LLMFailureKind.InvalidResponse);
            logger.LogInformation("Local chat completed with {Model}, {Characters} characters",options.Model,count);
        }
        finally { chatSlots.Release(); }
    }

    private static async Task<bool> NextAsync(IAsyncEnumerator<ChatResponseUpdate> stream,CancellationToken caller)
    {
        try { return await stream.MoveNextAsync(); }
        catch(OperationCanceledException) when(!caller.IsCancellationRequested) { throw new LLMServiceException(LLMFailureKind.Timeout); }
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException || e.GetType().Namespace?.StartsWith("OllamaSharp",StringComparison.Ordinal)==true)
        { throw new LLMServiceException(LLMFailureKind.Unavailable); }
    }

    public async Task<AIChatHealth> HealthAsync(CancellationToken ct)
    {
        ValidateChat([new("user","health")],null);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response=await client.GetAsync("api/tags",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode) return new(false,options.Model);
            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes,timeout.Token);
            var bytes=await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if(bytes.Length>MaximumResponseBytes) return new(false,options.Model);
            using var json=JsonDocument.Parse(bytes);
            var names=json.RootElement.GetProperty("models").EnumerateArray().Where(m=>!m.TryGetProperty("remote_host",out _)).Select(m=>m.GetProperty("name").GetString()).ToArray();
            return new(names.Contains(options.Model),options.Model);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { return new(false,options.Model); }
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return new(false,options.Model); }
    }
}
