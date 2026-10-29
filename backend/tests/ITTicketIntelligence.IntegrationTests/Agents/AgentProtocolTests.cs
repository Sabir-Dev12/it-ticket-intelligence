using System.Net;
using System.Text.Json;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Infrastructure.LanguageModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Agents;

public sealed class AgentProtocolTests
{
    private const string ChunkId = "11111111-1111-4111-8111-111111111111";

    [Theory]
    [InlineData(LLMProvider.Ollama, "agent-success")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-success")]
    [InlineData(LLMProvider.Ollama, "agent-retry-once")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-retry-once")]
    [InlineData(LLMProvider.Ollama, "agent-unavailable")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-unavailable")]
    [InlineData(LLMProvider.Ollama, "agent-timeout")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-timeout")]
    [InlineData(LLMProvider.Ollama, "agent-refused")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-refused")]
    [InlineData(LLMProvider.Ollama, "agent-truncated")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-truncated")]
    [InlineData(LLMProvider.Ollama, "agent-malformed")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-malformed")]
    [InlineData(LLMProvider.Ollama, "agent-final-tool")]
    [InlineData(LLMProvider.OpenAICompatible, "agent-final-tool")]
    public async Task RealHttpToolRoundTripAndStructuredFinal(LLMProvider provider, string mode)
    {
        var external = Environment.GetEnvironmentVariable("LLM_TEST_BASE_URL");
        await using var server = string.IsNullOrWhiteSpace(external) ? await FakeServer.Start() : null;
        var options = LLMOptions.FromEnvironment(key => key switch
        {
            "LLM__Provider" => provider.ToString(), "LLM__BaseUrl" => (external ?? server!.Url).TrimEnd('/') + "/" + provider.ToString().ToLowerInvariant() + "/",
            "LLM__Model" => mode, "LLM__ApiKey" => "test-only-credential", "LLM__AttemptTimeoutSeconds" => "1",
            "LLM__TotalTimeoutSeconds" => "5", "LLM__MaxRetries" => mode == "agent-timeout" ? "0" : "2", "LLM__RetryDelayMilliseconds" => "0", _ => null
        });
        using var handler = new CountingHandler();
        using var service = new HttpLLMService(new HttpClient(handler), options, TimeProvider.System, NullLogger<HttpLLMService>.Instance);
        var messages = new List<AgentMessage> { new("system", "Use read-only tools. Return JSON during the final phase."),
            new("user", "{\"task\":\"Investigate\",\"retrievedContext\":[{\"chunkId\":\"" + ChunkId + "\",\"text\":\"Verify TCP port 1433.\"}]}") };
        if (mode is "agent-unavailable" or "agent-timeout")
        {
            var failure = await Assert.ThrowsAsync<LLMServiceException>(() => service.CompleteTurnAsync(new(messages, false)));
            Assert.Equal(mode == "agent-timeout" ? LLMFailureKind.Timeout : LLMFailureKind.Unavailable, failure.Kind);
            Assert.Equal(mode == "agent-timeout" ? 1 : 3, handler.Count); return;
        }
        if (mode is "agent-refused" or "agent-truncated")
        {
            var failure = await Assert.ThrowsAsync<LLMServiceException>(() => service.CompleteTurnAsync(new(messages, false)));
            Assert.Equal(mode == "agent-refused" ? LLMFailureKind.Refused : LLMFailureKind.InvalidResponse, failure.Kind);
            Assert.Equal(1, handler.Count); return;
        }
        var turn = await service.CompleteTurnAsync(new(messages.ToArray(), false));
        var call = Assert.Single(turn.ToolCalls); Assert.Equal("get_ticket_history", call.Name); Assert.Equal("{}", call.ArgumentsJson);
        Assert.Equal(mode == "agent-retry-once" ? 2 : 1, turn.Attempts);
        messages.Add(new("assistant", "", turn.ToolCalls));
        messages.Add(new("tool", "[]", ToolCallId: call.Id, ToolName: call.Name));
        var ready = await service.CompleteTurnAsync(new(messages.ToArray(), false));
        Assert.Empty(ready.ToolCalls); Assert.Null(ready.Draft);
        messages.Add(new("user", "Return the final JSON now."));
        if (mode is "agent-malformed" or "agent-final-tool")
        {
            var failure = await Assert.ThrowsAsync<LLMServiceException>(() => service.CompleteTurnAsync(new(messages.ToArray(), true)));
            Assert.Equal(LLMFailureKind.InvalidResponse, failure.Kind); return;
        }
        var final = await service.CompleteTurnAsync(new(messages.ToArray(), true));
        Assert.NotNull(final.Draft); Assert.Empty(final.ToolCalls); Assert.Equal(ChunkId, Assert.Single(final.Draft.CitedChunkIds));
        Assert.Equal(.9, final.Draft.Confidence);
        Assert.Equal(mode == "agent-retry-once" ? 6 : 3, handler.Count);
        var toolBody = handler.Bodies.First(); var finalBody = handler.Bodies.Last();
        Assert.Equal(6, toolBody.GetProperty("tools").GetArrayLength()); Assert.False(finalBody.TryGetProperty("tools", out _));
        Assert.False(toolBody.TryGetProperty("response_format", out _)); Assert.False(toolBody.TryGetProperty("format", out _));
        var schema = provider == LLMProvider.Ollama ? finalBody.GetProperty("format") : finalBody.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var resultMessage = finalBody.GetProperty("messages")[3];
        Assert.Equal(provider == LLMProvider.Ollama ? call.Name : call.Id, resultMessage.GetProperty(provider == LLMProvider.Ollama ? "tool_name" : "tool_call_id").GetString());
    }

    private sealed class CountingHandler : DelegatingHandler
    {
        public int Count { get; private set; }
        public List<JsonElement> Bodies { get; } = [];
        public CountingHandler() : base(new SocketsHttpHandler { AllowAutoRedirect = false }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Bodies.Add(body.RootElement.Clone());
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FakeServer(WebApplication app, string url) : IAsyncDisposable
    {
        public string Url { get; } = url;
        public static async Task<FakeServer> Start()
        {
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build(); var counts = new Dictionary<string, int>();
            app.MapPost("/{**path}", async context =>
            {
                using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                var body = doc.RootElement; var mode = body.GetProperty("model").GetString()!;
                var ollama = context.Request.Path.Value!.EndsWith("/api/chat", StringComparison.Ordinal);
                var key = mode + context.Request.Path; var count = counts.GetValueOrDefault(key) + 1; counts[key] = count;
                if (mode == "agent-unavailable" || mode == "agent-retry-once" && count % 2 == 1)
                { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Synthetic unavailable" }); return; }
                if (mode == "agent-timeout") { await Task.Delay(3000, context.RequestAborted); return; }
                var final = !body.TryGetProperty("tools", out _);
                var hasResult = body.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("role").GetString() == "tool");
                var calls = (!final && !hasResult) || (final && mode == "agent-final-tool");
                object message;
                if (mode == "agent-refused") message = new { role = "assistant", content = "", refusal = "Synthetic refusal" };
                else if (calls) message = ollama
                    ? (object)new { role = "assistant", content = "", tool_calls = new[] { new { type = "function", function = new { name = "get_ticket_history", arguments = new { } } } } }
                    : new { role = "assistant", content = "", tool_calls = new[] { new { id = "call-history", type = "function", function = new { name = "get_ticket_history", arguments = "{}" } } } };
                else
                {
                    var content = !final ? "Ready for the final phase." : mode == "agent-malformed" ? "{\"summary\":\"missing fields\"}" : JsonSerializer.Serialize(new
                    { summary = "SQL timeout", diagnosis = "Connection path may be blocked", possibleCauses = new[] { "Firewall configuration" },
                      recommendedActions = new[] { "Ask an operator to verify TCP 1433" }, citedChunkIds = new[] { ChunkId }, confidence = .9, insufficientContext = false });
                    message = new { role = "assistant", content };
                }
                await context.Response.WriteAsJsonAsync(ollama ? (object)new { model = mode, done = true, done_reason = mode == "agent-truncated" ? "length" : "stop", message }
                    : new { model = mode, choices = new[] { new { finish_reason = mode == "agent-truncated" ? "length" : calls ? "tool_calls" : "stop", message } } });
            });
            await app.StartAsync();
            return new FakeServer(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
