using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Api.Realtime;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Application.Realtime;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Realtime;

[Collection("Postgres")]
public sealed class TicketHubTests(PostgresFixture fixture)
{
    private const string Token = "signalr-test-token-0123456789abcdef0123456789";
    private const string OtherToken = "signalr-other-token-0123456789abcdef0123456789";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(TicketEventName.TicketCreated)]
    [InlineData(TicketEventName.TicketUpdated)]
    [InlineData(TicketEventName.TicketAssigned)]
    [InlineData(TicketEventName.TicketStatusChanged)]
    [InlineData(TicketEventName.CommentAdded)]
    [InlineData(TicketEventName.PredictionCompleted)]
    [InlineData(TicketEventName.AIAnalysisCompleted)]
    public async Task ApplicationCommitsBeforePublishingEveryRequestedEvent(TicketEventName name)
    {
        var seed = await Seed(); await using var factory = Factory(seed.Ticket.Id, all: true);
        await using var hub = Connection(factory); var events = Listen(hub);
        await hub.StartAsync(); await hub.InvokeAsync<TicketDto>("JoinTicket", seed.Ticket.Id);
        using var http = factory.CreateClient();
        using var response = name switch
        {
            TicketEventName.TicketCreated => await http.PostAsJsonAsync("/api/tickets", new { title = "Realtime ticket", description = "Created while observing", createdById = seed.UserId }),
            TicketEventName.TicketUpdated => await http.PutAsJsonAsync($"/api/tickets/{seed.Ticket.Id}", new { title = "Updated over REST", description = "New reported details", revision = 1 }),
            TicketEventName.TicketAssigned => await http.PatchAsJsonAsync($"/api/tickets/{seed.Ticket.Id}/assignment", new { teamId = seed.TeamId, revision = 1 }),
            TicketEventName.TicketStatusChanged => await http.PatchAsJsonAsync($"/api/tickets/{seed.Ticket.Id}/status", new { status = "InProgress", revision = 1 }),
            TicketEventName.CommentAdded => await http.PostAsJsonAsync($"/api/tickets/{seed.Ticket.Id}/comments", new { authorId = seed.UserId, message = "New operator observation", revision = 1 }),
            TicketEventName.PredictionCompleted => await http.PostAsync($"/api/tickets/{seed.Ticket.Id}/analyze", null),
            _ => await http.PostAsync($"/api/tickets/{seed.Ticket.Id}/investigate", null)
        };
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var notification = await Receive(events, name);
        Assert.NotEqual(Guid.Empty, notification.EventId); Assert.True(notification.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        await using var db = fixture.CreateContext();
        var current = await db.Tickets.SingleAsync(t => t.Id == notification.TicketId);
        Assert.Equal(current.Revision, notification.TicketRevision);
        if (name == TicketEventName.PredictionCompleted)
        {
            Assert.NotNull(notification.RelatedId);
            Assert.Equal(3, await db.TicketPredictions.CountAsync(p => p.AnalysisId == notification.RelatedId));
        }
        if (name == TicketEventName.AIAnalysisCompleted)
        {
            var id = notification.RelatedId!.Value;
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM app.ticket_agent_analyses WHERE id = {id}").SingleAsync());
        }
        var snapshot = await hub.InvokeAsync<TicketDto>("GetTicket", notification.TicketId);
        Assert.Equal(current.Revision, snapshot.Revision);
        Assert.DoesNotContain("description", JsonSerializer.Serialize(notification, Json));
    }

    [Fact]
    public async Task GroupsAreIsolatedAndRejoiningAfterReconnectRestoresUpdates()
    {
        var a = await Seed(); var b = await Seed(); await using var factory = Factory(a.Ticket.Id, otherId: b.Ticket.Id);
        await using var first = Connection(factory); await using var other = Connection(factory, OtherToken);
        var firstEvents = Listen(first); var otherEvents = Listen(other);
        await first.StartAsync(); await other.StartAsync();
        await first.InvokeAsync<TicketDto>("JoinTicket", a.Ticket.Id); await other.InvokeAsync<TicketDto>("JoinTicket", b.Ticket.Id);
        await Assert.ThrowsAsync<HubException>(() => first.InvokeAsync<TicketDto>("JoinTicket", b.Ticket.Id));
        await Assert.ThrowsAsync<HubException>(() => first.InvokeAsync<TicketDto>("GetTicket", b.Ticket.Id));
        using var http = factory.CreateClient();
        using var editA = await http.PutAsJsonAsync($"/api/tickets/{a.Ticket.Id}", new { title = "Updated A", description = "Private A observation", revision = 1 });
        using var editB = await http.PutAsJsonAsync($"/api/tickets/{b.Ticket.Id}", new { title = "Updated B", description = "Private B observation", revision = 1 });
        Assert.Equal(a.Ticket.Id, (await Receive(firstEvents, TicketEventName.TicketUpdated)).TicketId);
        Assert.Equal(b.Ticket.Id, (await Receive(otherEvents, TicketEventName.TicketUpdated)).TicketId);
        Assert.False(otherEvents.Reader.TryRead(out _)); Assert.False(firstEvents.Reader.TryRead(out _));
        await first.StopAsync();
        using var whileOffline = await http.PutAsJsonAsync($"/api/tickets/{a.Ticket.Id}", new { title = "Changed offline", description = "Resync required", revision = 2 });
        await first.StartAsync();
        var recovered = await first.InvokeAsync<TicketDto>("JoinTicket", a.Ticket.Id);
        Assert.Equal(3, recovered.Revision); Assert.Equal("Changed offline", recovered.Title);
        using var afterReconnect = await http.PutAsJsonAsync($"/api/tickets/{a.Ticket.Id}", new { title = "Changed online", description = "After rejoin", revision = 3 });
        Assert.Equal(4, (await Receive(firstEvents, TicketEventName.TicketUpdated)).TicketRevision);
    }

    [Fact]
    public async Task LeavingAGroupAndFailedWritesDoNotProduceEvents()
    {
        var seed = await Seed(); await using var factory = Factory(seed.Ticket.Id);
        await using var hub = Connection(factory); var events = Listen(hub);
        await hub.StartAsync(); await hub.InvokeAsync<TicketDto>("JoinTicket", seed.Ticket.Id);
        using var http = factory.CreateClient();
        using var rejected = await http.PutAsJsonAsync($"/api/tickets/{seed.Ticket.Id}", new { title = "Rejected", description = "Wrong revision", revision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var accepted = await http.PutAsJsonAsync($"/api/tickets/{seed.Ticket.Id}", new { title = "Accepted", description = "Current revision", revision = 1 });
        Assert.Equal(2, (await Receive(events, TicketEventName.TicketUpdated)).TicketRevision); Assert.False(events.Reader.TryRead(out _));
        await hub.InvokeAsync("LeaveTicket", seed.Ticket.Id);
        using var afterLeave = await http.PutAsJsonAsync($"/api/tickets/{seed.Ticket.Id}", new { title = "Not subscribed", description = "After leave", revision = 2 });
        var read = await hub.InvokeAsync<TicketDto>("GetTicket", seed.Ticket.Id); Assert.Equal(3, read.Revision);
        Assert.False(events.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAuthenticationOrReadPermissionRefusesTheConnection(bool noPermission)
    {
        await using var factory = Factory(Guid.NewGuid(), noPermission: noPermission);
        await using var hub = Connection(factory, noPermission ? Token : "");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.Equal(noPermission ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task BrowserWebSocketQueryTokenWorksButQueryCredentialsAreRejectedOnOtherRoutes()
    {
        var seed = await Seed(); await using var factory = Factory(seed.Ticket.Id);
        await using var hub = Connection(factory, browserSocket: true); await hub.StartAsync();
        Assert.Equal(seed.Ticket.Id, (await hub.InvokeAsync<TicketDto>("JoinTicket", seed.Ticket.Id)).Id);
        using var http = factory.CreateClient();
        using var negotiate = await http.PostAsync(TicketHub.Route + "/negotiate?negotiateVersion=1&access_token=" + Token, null);
        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
        using var mcp = await http.PostAsJsonAsync("/mcp?access_token=" + Token, new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var origin = await http.PostAsync(TicketHub.Route + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
    }

    private WebApplicationFactory<Api.Program> Factory(Guid id, bool all = false, Guid? otherId = null, bool noPermission = false) =>
        new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<McpSecurityOptions>();
                List<McpCredential> profiles = [new(Hash(Token), new McpCaller("realtime-reader", noPermission ? [] : ["tickets.read"], [id], all))];
                if (otherId.HasValue) profiles.Add(new(Hash(OtherToken), new McpCaller("other-reader", ["tickets.read"], [otherId.Value])));
                s.AddSingleton(new McpSecurityOptions(profiles));
                s.RemoveAll<IRagRetriever>(); s.AddSingleton<IRagRetriever, FakeAi>(); s.RemoveAll<ILLMAgentService>(); s.AddSingleton<ILLMAgentService, FakeAi>();
            });
        });
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static HubConnection Connection(WebApplicationFactory<Api.Program> factory, string token = Token, bool browserSocket = false)
    {
        _ = factory.CreateClient();
        return new HubConnectionBuilder().WithUrl(new Uri(factory.Server.BaseAddress, TicketHub.Route), o =>
        {
            o.Transports = browserSocket ? HttpTransportType.WebSockets : HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler(); o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            if (browserSocket) o.WebSocketFactory = (context, ct) =>
            {
                var socket = factory.Server.CreateWebSocketClient(); var builder = new UriBuilder(context.Uri);
                builder.Query = builder.Query.TrimStart('?') + "&access_token=" + Uri.EscapeDataString(token);
                return new ValueTask<WebSocket>(socket.ConnectAsync(builder.Uri, ct));
            };
        }).AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter())).Build();
    }
    private static Channel<TicketEvent> Listen(HubConnection hub)
    {
        var channel = Channel.CreateUnbounded<TicketEvent>();
        foreach (var name in Enum.GetValues<TicketEventName>()) hub.On<TicketEvent>(name.ToString(), e => channel.Writer.TryWrite(e));
        return channel;
    }
    private static async Task<TicketEvent> Receive(Channel<TicketEvent> events, TicketEventName name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { var value = await events.Reader.ReadAsync(timeout.Token); if (value.Name == name) return value; }
    }
    private async Task<(Ticket Ticket, Guid UserId, Guid TeamId)> Seed()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1); var key = Guid.NewGuid();
        var user = new User(Guid.NewGuid(), "SignalR test", EmailAddress.Create(key.ToString("N") + "@example.com"), UserRole.User, now);
        var team = new Team(Guid.NewGuid(), "Realtime-" + key.ToString("N"), now);
        var ticket = new Ticket(Guid.NewGuid(), "RT-" + key.ToString("N")[..20], "SQL timeout", "Reported timeout after deployment", user.Id, now);
        await using var db = fixture.CreateContext(); db.Users.Add(user); db.Teams.Add(team); db.Tickets.Add(ticket); await db.SaveChangesAsync();
        return (ticket, user.Id, team.Id);
    }
    private sealed class FakeAi : IRagRetriever, ILLMAgentService
    {
        public Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new RetrievedContext("fake", []));
        public Task<AgentLLMTurn> CompleteTurnAsync(AgentLLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new AgentLLMTurn([], request.Finalize
            ? new AgentDraft("Reported connection timeout", "Further evidence is required", [], ["Ask an operator for connectivity diagnostics"], [], 0, true) : null, "Fake", "test", 1));
    }
}
