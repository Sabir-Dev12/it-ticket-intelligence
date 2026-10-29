using System.Net;
using System.Text.Json;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Infrastructure;
using ITTicketIntelligence.Infrastructure.LanguageModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.LanguageModels;

public sealed class LLMWireTests
{
    [Theory]
    [InlineData(LLMProvider.Ollama,"success")]
    [InlineData(LLMProvider.OpenAICompatible,"success")]
    [InlineData(LLMProvider.Ollama,"retry-once")]
    [InlineData(LLMProvider.OpenAICompatible,"retry-once")]
    [InlineData(LLMProvider.Ollama,"unavailable")]
    [InlineData(LLMProvider.OpenAICompatible,"unavailable")]
    [InlineData(LLMProvider.Ollama,"timeout")]
    [InlineData(LLMProvider.OpenAICompatible,"timeout")]
    public async Task RealHttpAdapterAgainstFakeLlm(LLMProvider provider, string mode)
    {
        // An optional external URL runs these same tests against the repository's Postman simulation.
        var external = Environment.GetEnvironmentVariable("LLM_TEST_BASE_URL");
        await using var fake = string.IsNullOrWhiteSpace(external) ? await FakeServer.Start() : null;
        var baseUrl = external ?? fake!.Url;
        var options = LLMOptions.FromEnvironment(key => key switch
        {
            "LLM__Provider" => provider.ToString(), "LLM__BaseUrl" => baseUrl.TrimEnd('/') + "/" + provider.ToString().ToLowerInvariant() + "/",
            "LLM__Model" => mode, "LLM__ApiKey" => "test-only-credential", "LLM__MaxRetries" => mode == "timeout" ? "0" : "2",
            "LLM__AttemptTimeoutSeconds" => "1", "LLM__TotalTimeoutSeconds" => "5", "LLM__RetryDelayMilliseconds" => "0", _ => null
        });
        using var counting = new CountRequests(new SocketsHttpHandler { AllowAutoRedirect=false });
        using var service = new HttpLLMService(new HttpClient(counting),options,TimeProvider.System,NullLogger<HttpLLMService>.Instance);
        if (mode is "timeout" or "unavailable")
        {
            var failure = await Assert.ThrowsAsync<LLMServiceException>(()=>service.GenerateAsync(HttpLLMServiceTests.Request()));
            Assert.Equal(mode == "timeout" ? LLMFailureKind.Timeout : LLMFailureKind.Unavailable,failure.Kind);
            Assert.Equal(mode == "timeout" ? 1 : 3,counting.Count);
        }
        else
        {
            var answer = await service.GenerateAsync(HttpLLMServiceTests.Request());
            Assert.Equal(mode == "retry-once" ? 2 : 1,answer.Attempts); Assert.Equal(answer.Attempts,counting.Count);
            Assert.Equal(HttpLLMServiceTests.ChunkId,Assert.Single(answer.Citations).ChunkId);
            Assert.True(answer.NeedsHumanReview); Assert.False(answer.Answer.InsufficientContext);
        }
    }

    [Fact]
    public void DependencyInjectionAcceptsAFakeAndDoesNotResolveTheDatabase()
    {
        var services = new ServiceCollection(); var fake = new FakeService(); services.AddSingleton<ILLMService>(fake);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["ConnectionStrings:Postgres"]="Host=unreachable.invalid;Database=must-not-open;Username=not-used" }).Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
        Assert.Same(fake,provider.GetRequiredService<ILLMService>());
    }

    private sealed class CountRequests(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Count {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Count++;return base.SendAsync(request,token); }
    }
    private sealed class FakeService : ILLMService
    { public Task<LLMResponse> GenerateAsync(LLMRequest request,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); }

    private sealed class FakeServer(WebApplication app,string url) : IAsyncDisposable
    {
        public string Url {get;}=url;
        public static async Task<FakeServer> Start()
        {
            var builder = WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();var counts = new Dictionary<string,int>();
            app.MapPost("/{**path}",async (HttpContext context)=>
            {
                using var request = await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:context.RequestAborted);
                var body=request.RootElement;var mode=body.GetProperty("model").GetString()!;
                var compatible=body.TryGetProperty("response_format",out _);var key=context.Request.Path+"/"+mode;
                var count=counts.GetValueOrDefault(key)+1;counts[key]=count;
                if (mode=="timeout")
                {
                    try { await Task.Delay(3000,context.RequestAborted); } catch (OperationCanceledException) { return; }
                }
                if (mode=="unavailable" || (mode=="retry-once" && count%2==1)) {context.Response.StatusCode=503;return;}
                Assert.False(body.GetProperty("stream").GetBoolean());
                var response=HttpLLMServiceTests.Envelope(compatible?LLMProvider.OpenAICompatible:LLMProvider.Ollama,mode);
                await context.Response.WriteAsJsonAsync(response,cancellationToken:context.RequestAborted);
            });
            await app.StartAsync();var addresses=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new(app,addresses.Addresses.Single());
        }
        public async ValueTask DisposeAsync() {await app.StopAsync();await app.DisposeAsync();}
    }
}
