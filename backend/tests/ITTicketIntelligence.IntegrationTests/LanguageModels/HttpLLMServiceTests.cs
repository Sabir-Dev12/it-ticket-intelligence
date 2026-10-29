using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.LanguageModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.LanguageModels;

public sealed class HttpLLMServiceTests
{
    internal static readonly Guid DocumentId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static readonly Guid ChunkId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static LLMRequest Request() => new("Investigate SQL connection timeout", new RetrievedContext("test", [
        new(DocumentId,"SQL guide",ChunkId,"Check TCP port 1433.",.9,
            new KnowledgeChunkMetadata(DocumentId,2,"SQL guide","kb/sql",TicketCategory.Database,
                KnowledgeDocumentKind.Procedure,KnowledgeContentFormat.PlainText,0,0,20,new string('a',64))) ]));
    internal static object ValidAnswer() => new { summary = "Check SQL connectivity.", suggestedSteps = new[] { "Verify TCP port 1433." },
        citedChunkIds = new[] { ChunkId.ToString() }, insufficientContext = false };
    internal static object Envelope(LLMProvider provider, string model, object? answer = null, string finish = "stop")
    {
        var content = answer is string raw ? raw : JsonSerializer.Serialize(answer ?? ValidAnswer());
        return provider == LLMProvider.Ollama ? new { model, done = true, done_reason = finish, message = new { role = "assistant", content } } :
            new { model, choices = new[] { new { finish_reason = finish, message = new { role = "assistant", content } } } };
    }
    internal static LLMOptions Options(LLMProvider provider = LLMProvider.Ollama, int retries = 2, int attemptTimeout = 2, int totalTimeout = 10) =>
        new() { Provider = provider, BaseUrl = "https://llm.test/proxy/", Model = "test-model", ApiKey = "test-only-credential",
            MaxRetries = retries, AttemptTimeoutSeconds = attemptTimeout, TotalTimeoutSeconds = totalTimeout, RetryDelayMilliseconds = 0 };
    private static HttpLLMService Service(HttpMessageHandler handler, LLMOptions? options = null, ILogger<HttpLLMService>? log = null) =>
        new(new HttpClient(handler), options ?? Options(), TimeProvider.System, log ?? NullLogger<HttpLLMService>.Instance);

    [Theory]
    [InlineData(LLMProvider.Ollama)]
    [InlineData(LLMProvider.OpenAICompatible)]
    public async Task BothProvidersSendStructuredContextAndReceiveValidatedTypedAnswers(LLMProvider provider)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(Envelope(provider,"provider-model-revision"))));
        using var service = Service(handler, Options(provider)); var result = await service.GenerateAsync(Request());
        Assert.Equal(1, handler.Calls); Assert.Equal("provider-model-revision",result.Model); Assert.Equal(1,result.Attempts);
        Assert.Equal("Check SQL connectivity.",result.Answer.Summary); Assert.True(result.NeedsHumanReview);
        Assert.Equal(new LLMCitation(DocumentId,ChunkId,2,"SQL guide","kb/sql",new string('a',64)),Assert.Single(result.Citations));
        var sent = handler.Bodies[0];
        Assert.False(sent.GetProperty("stream").GetBoolean()); Assert.False(sent.TryGetProperty("tools",out _));
        var user = JsonDocument.Parse(sent.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement;
        Assert.Equal(Request().Prompt,user.GetProperty("prompt").GetString()); Assert.Equal(ChunkId,user.GetProperty("context")[0].GetProperty("chunkId").GetGuid());
        Assert.Equal("Bearer test-only-credential",handler.Authorization);
        if (provider == LLMProvider.Ollama) { Assert.Equal("/proxy/api/chat",handler.Paths[0]); Assert.Equal("object",sent.GetProperty("format").GetProperty("type").GetString()); }
        else { Assert.Equal("/proxy/chat/completions",handler.Paths[0]); Assert.True(sent.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean()); Assert.True(sent.TryGetProperty("max_completion_tokens",out _)); }
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task TransientFailuresRetryWithIdenticalRequestAndStopAfterSuccess(int code)
    {
        var handler = new Handler((call, _) => Task.FromResult(call < 3 ? new HttpResponseMessage((HttpStatusCode)code) : Json(Envelope(LLMProvider.Ollama,"test"))));
        using var service = Service(handler); var result = await service.GenerateAsync(Request());
        Assert.Equal(3,result.Attempts); Assert.Equal(3,handler.Calls);
        Assert.Single(handler.RawBodies.Distinct());
    }

    [Fact]
    public async Task ExhaustedRetriesAreBoundedAndLongRetryAfterIsNotRetriedEarly()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using (var service = Service(handler)) { var ex = await Assert.ThrowsAsync<LLMServiceException>(() => service.GenerateAsync(Request())); Assert.Equal(LLMFailureKind.Unavailable,ex.Kind); Assert.Equal(3,handler.Calls); }
        var limited = new Handler((_, _) => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromMinutes(2)); return Task.FromResult(r); });
        using var rateLimited = Service(limited);
        Assert.Equal(LLMFailureKind.RateLimited,(await Assert.ThrowsAsync<LLMServiceException>(() => rateLimited.GenerateAsync(Request()))).Kind);
        Assert.Equal(1,limited.Calls);
    }

    [Theory]
    [InlineData(400, LLMFailureKind.ProviderRejected)]
    [InlineData(401, LLMFailureKind.Authentication)]
    [InlineData(403, LLMFailureKind.Authentication)]
    [InlineData(404, LLMFailureKind.ProviderRejected)]
    [InlineData(501, LLMFailureKind.ProviderRejected)]
    public async Task PermanentHttpFailuresAreNotRetried(int code, LLMFailureKind kind)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("sensitive-provider-body") }));
        using var service = Service(handler); var ex = await Assert.ThrowsAsync<LLMServiceException>(() => service.GenerateAsync(Request()));
        Assert.Equal(kind,ex.Kind); Assert.Equal(1,handler.Calls); Assert.DoesNotContain("sensitive-provider-body",ex.ToString());
    }

    [Theory]
    [InlineData("{malformed")]
    [InlineData("{\"summary\":\"Answer\",\"suggestedSteps\":[],\"citedChunkIds\":[]}")]
    [InlineData("{\"summary\":\"Answer\",\"suggestedSteps\":[],\"citedChunkIds\":[\"99999999-9999-4999-8999-999999999999\"],\"insufficientContext\":false}")]
    [InlineData("{\"summary\":\"Answer\",\"suggestedSteps\":[],\"citedChunkIds\":[],\"insufficientContext\":true,\"executeSql\":true}")]
    [InlineData("{\"summary\":\"A\",\"summary\":\"B\",\"suggestedSteps\":[],\"citedChunkIds\":[],\"insufficientContext\":true}")]
    [InlineData("{\"summary\":\"Answer\",\"suggestedSteps\":[],\"citedChunkIds\":[],\"insufficientContext\":false}")]
    public async Task MalformedUngroundedOrAmbiguousAnswersAreNotRetried(string body)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(Envelope(LLMProvider.Ollama,"test",body))));
        using var service = Service(handler); var ex = await Assert.ThrowsAsync<LLMServiceException>(() => service.GenerateAsync(Request()));
        Assert.Equal(LLMFailureKind.InvalidResponse,ex.Kind); Assert.Equal(1,handler.Calls);
    }

    [Fact]
    public async Task EmptyContextRequiresAnExplicitInsufficientContextAnswer()
    {
        var answer = new { summary = "More evidence is needed.",suggestedSteps = Array.Empty<string>(),citedChunkIds = Array.Empty<string>(),insufficientContext = true };
        using var service = Service(new Handler((_, _) => Task.FromResult(Json(Envelope(LLMProvider.Ollama,"test",answer)))));
        Assert.True((await service.GenerateAsync(new("Investigate"))).Answer.InsufficientContext);
    }

    [Fact]
    public async Task TruncationRefusalAndToolCallsAreRejected()
    {
        var truncatedHandler = new Handler((_, _) => Task.FromResult(Json(Envelope(LLMProvider.Ollama,"test",finish:"length"))));
        using (var truncated = Service(truncatedHandler))
            Assert.Equal(LLMFailureKind.InvalidResponse,(await Assert.ThrowsAsync<LLMServiceException>(() => truncated.GenerateAsync(Request()))).Kind);
        using (var refused = Service(new Handler((_, _) => Task.FromResult(Json(new {model="test",choices=new[] {new {finish_reason="stop",message=new {role="assistant",content=(string?)null,refusal="declined"}}}}))),Options(LLMProvider.OpenAICompatible)))
            Assert.Equal(LLMFailureKind.Refused,(await Assert.ThrowsAsync<LLMServiceException>(() => refused.GenerateAsync(Request()))).Kind);
        using var tools = Service(new Handler((_, _) => Task.FromResult(Json(new {model="test",done=true,done_reason="stop",message=new {role="assistant",content=JsonSerializer.Serialize(ValidAnswer()),tool_calls=new[]{new {name="query_database"}}}}))));
        Assert.Equal(LLMFailureKind.InvalidResponse,(await Assert.ThrowsAsync<LLMServiceException>(() => tools.GenerateAsync(Request()))).Kind);
    }

    [Fact]
    public async Task TimeoutCancellationNetworkFailureAndOversizedBodiesAreHandled()
    {
        var hanging = new Handler(async (_,ct) => { await Task.Delay(Timeout.Infinite,ct); return Json(new{}); });
        using (var service = Service(hanging,Options(retries:0,attemptTimeout:1,totalTimeout:2)))
            Assert.Equal(LLMFailureKind.Timeout,(await Assert.ThrowsAsync<LLMServiceException>(() => service.GenerateAsync(Request()))).Kind);
        var cancelled = new Handler(async (_,ct) => { await Task.Delay(Timeout.Infinite,ct); return Json(new{}); });
        using (var service = Service(cancelled))
        { using var cts = new CancellationTokenSource(50); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.GenerateAsync(Request(),cts.Token)); Assert.Equal(1,cancelled.Calls); }
        var network = new Handler((call,_) => call == 1 ? throw new HttpRequestException("network test") : Task.FromResult(Json(Envelope(LLMProvider.Ollama,"test"))));
        using (var service = Service(network)) Assert.Equal(2,(await service.GenerateAsync(Request())).Attempts);
        var huge = new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',HttpLLMService.MaximumResponseBytes+1))}));
        using (var service = Service(huge)) Assert.Equal(LLMFailureKind.InvalidResponse,(await Assert.ThrowsAsync<LLMServiceException>(()=>service.GenerateAsync(Request()))).Kind);
    }

    [Fact]
    public async Task GlobalBudgetAlsoBoundsRetryDelayAndPreCancelledCallsNeverSend()
    {
        var handler = new Handler((_,_) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(2)); return Task.FromResult(response);
        });
        using var service = Service(handler, Options(attemptTimeout:1,totalTimeout:1));
        Assert.Equal(LLMFailureKind.Timeout,(await Assert.ThrowsAsync<LLMServiceException>(()=>service.GenerateAsync(Request()))).Kind);
        Assert.Equal(1,handler.Calls);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.GenerateAsync(Request(),cts.Token));
        Assert.Equal(1,handler.Calls);
    }

    [Fact]
    public async Task LogsContainOperationalMetadataAndExcludeSecretsPromptsAndBodies()
    {
        var logger = new CaptureLogger();
        using var service = Service(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("provider-private-body")})),log:logger);
        await Assert.ThrowsAsync<LLMServiceException>(()=>service.GenerateAsync(Request()));
        var messages = string.Join('\n',logger.Messages);
        Assert.Contains("retry",messages); Assert.Contains("failed",messages);
        Assert.DoesNotContain("test-only-credential",messages); Assert.DoesNotContain(Request().Prompt,messages); Assert.DoesNotContain("provider-private-body",messages);
    }

    [Fact]
    public void ConfigurationUsesEnvironmentNamesAndRejectsInvalidValuesWithoutEchoingSecrets()
    {
        var values = new Dictionary<string,string?> { ["LLM__Provider"]="OpenAICompatible",["LLM__Model"]="test-model",["LLM__ApiKey"]="test-only-credential" };
        var options = LLMOptions.FromEnvironment(key=>values.GetValueOrDefault(key));
        Assert.Equal(LLMProvider.OpenAICompatible,options.Provider); Assert.DoesNotContain("test-only-credential",options.ToString());
        Assert.DoesNotContain("test-only-credential",JsonSerializer.Serialize(options));
        values["LLM__MaxRetries"]="3"; Assert.Throws<InvalidOperationException>(()=>LLMOptions.FromEnvironment(key=>values.GetValueOrDefault(key)));
        values["LLM__MaxRetries"]="2"; values["LLM__ApiKey"]=""; Assert.Throws<InvalidOperationException>(()=>LLMOptions.FromEnvironment(key=>values.GetValueOrDefault(key)));
        values["LLM__Provider"]="Unknown"; Assert.Throws<InvalidOperationException>(()=>LLMOptions.FromEnvironment(key=>values.GetValueOrDefault(key)));
    }

    [Fact]
    public void LlmComponentsCannotDependOnDbContextRepositoriesOrServiceLocation()
    {
        var types = typeof(HttpLLMService).Assembly.GetTypes().Where(t=>t.Namespace==typeof(HttpLLMService).Namespace);
        static bool Forbidden(Type t) => typeof(DbContext).IsAssignableFrom(t) || t==typeof(IServiceProvider) ||
            t.Name.Contains("Repository",StringComparison.Ordinal) || (t.IsGenericType && t.GetGenericArguments().Any(Forbidden));
        Assert.All(types,t=>
        {
            Assert.All(t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SelectMany(c=>c.GetParameters()),p=>Assert.False(Forbidden(p.ParameterType)));
            Assert.All(t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance),f=>Assert.False(Forbidden(f.FieldType)));
        });
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK){Content=JsonContent.Create(body)};
    private sealed class Handler(Func<int,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls {get;private set;} public List<string> Paths {get;}=[]; public List<JsonElement> Bodies {get;}=[]; public List<string> RawBodies {get;}=[]; public string? Authorization {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Calls++;Paths.Add(request.RequestUri!.AbsolutePath);Authorization=request.Headers.Authorization?.ToString();var raw=await request.Content!.ReadAsStringAsync(token);RawBodies.Add(raw);Bodies.Add(JsonDocument.Parse(raw).RootElement.Clone());return await respond(Calls,token); }
    }
    private sealed class CaptureLogger : ILogger<HttpLLMService>
    {
        public List<string> Messages {get;}=[]; public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null; public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,ex));
    }
}
