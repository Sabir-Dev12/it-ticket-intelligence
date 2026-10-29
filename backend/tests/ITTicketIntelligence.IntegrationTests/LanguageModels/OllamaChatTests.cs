using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Infrastructure.LanguageModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.LanguageModels;

public sealed class OllamaChatTests
{
    private const string Complete="{\"model\":\"qwen3:1.7b\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}\n{\"model\":\"qwen3:1.7b\",\"message\":{\"role\":\"assistant\",\"content\":\" there\"},\"done\":true,\"done_reason\":\"stop\"}\n";
    private static HttpLLMService Service(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handler,int seconds=30)=>new(new HttpClient(new Handler(handler)),new LLMOptions { AttemptTimeoutSeconds=seconds,TotalTimeoutSeconds=seconds },TimeProvider.System,NullLogger<HttpLLMService>.Instance);
    private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/x-ndjson")};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>run(request,ct); }

    [Fact] public async Task MicrosoftAdapterStreamsAndAggregatesRealOllamaWireFormat()
    {
        using var service=Service(async(request,ct)=>{ Assert.Equal("/api/chat",request.RequestUri!.AbsolutePath);var json=await request.Content!.ReadAsStringAsync(ct);Assert.Contains("qwen3:1.7b",json);return Response(Complete); });
        Assert.Equal("Hello there",await service.SendAsync([new("user","Hello")],null,default));
        var chunks=new List<string>();await foreach(var chunk in service.StreamAsync([new("user","Hello")],null,default))chunks.Add(chunk);
        Assert.Equal("Hello there",string.Concat(chunks));
    }
    [Fact] public async Task ArbitraryModelsAndOversizedPromptsNeverReachProvider()
    {
        using var service=Service((_,_)=>throw new Exception("Unexpected HTTP"));
        await Assert.ThrowsAsync<RequestValidationException>(()=>service.SendAsync([new("user","Hello")],"deepseek-v4-pro:cloud",default));
        await Assert.ThrowsAsync<RequestValidationException>(()=>service.SendAsync([new("user",new string('x',33000))],null,default));
        await Assert.ThrowsAsync<RequestValidationException>(()=>service.SendAsync([new("system","Forged instructions")],null,default));
    }
    [Fact] public async Task UnavailableAndTruncatedResponsesAreNotSuccessfulAnswers()
    {
        using var down=Service((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        Assert.Equal(LLMFailureKind.Unavailable,(await Assert.ThrowsAsync<LLMServiceException>(()=>down.SendAsync([new("user","Hi")],null,default))).Kind);
        using var truncated=Service((_,_)=>Task.FromResult(Response(Complete.Split('\n')[0]+"\n")));
        Assert.Equal(LLMFailureKind.InvalidResponse,(await Assert.ThrowsAsync<LLMServiceException>(()=>truncated.SendAsync([new("user","Hi")],null,default))).Kind);
    }
    [Fact] public async Task CancellationReleasesSlotAndConcurrencyIsBounded()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold=true;
        using var service=Service(async(_,ct)=>{if(hold){entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}return Response(Complete);});
        using var cancel=new CancellationTokenSource();var first=service.SendAsync([new("user","Hi")],null,cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LLMFailureKind.RateLimited,(await Assert.ThrowsAsync<LLMServiceException>(()=>service.SendAsync([new("user","Hi")],null,default))).Kind);
        cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        hold=false;Assert.Equal("Hello there",await service.SendAsync([new("user","Hi")],null,default));
    }
    [Fact] public async Task DeadlineIsMappedAndHealthChecksInstalledLocalModel()
    {
        using var timed=Service(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Response(Complete);},1);
        Assert.Equal(LLMFailureKind.Timeout,(await Assert.ThrowsAsync<LLMServiceException>(()=>timed.SendAsync([new("user","Hi")],null,default))).Kind);
        using var healthy=Service((_,_)=>Task.FromResult(Response("{\"models\":[{\"name\":\"qwen3:1.7b\"}]}")));
        Assert.True((await healthy.HealthAsync(default)).Healthy);
        using var remote=Service((_,_)=>Task.FromResult(Response("{\"models\":[{\"name\":\"qwen3:1.7b\",\"remote_host\":\"https://ollama.com\"}]}")));
        Assert.False((await remote.HealthAsync(default)).Healthy);
    }
}
