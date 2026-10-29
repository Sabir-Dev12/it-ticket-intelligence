using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Assistant;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Npgsql;

namespace ITTicketIntelligence.IntegrationTests.Assistant;

[Collection("Postgres")]
public sealed class AssistantApiTests(PostgresFixture fixture)
{
    private const string Token = "assistant-owner-test-token-0123456789abcdef";
    private const string OtherToken = "assistant-other-test-token-0123456789abcdef";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task QuestionUsesHistoryMlRagAndSavedSourcesThenRemembersThePreviousTurn()
    {
        var seed = await Seed(); var fake = new FakeAi(); await using var factory = Factory(seed, fake);
        await PrepareKnowledge(factory, fake); using var client = Client(factory);
        using var creation = await client.PostAsJsonAsync("/api/assistant/conversations", new { requestId = Guid.NewGuid(), question = $"Why is ticket #{seed.Reference[3..]} failing?" });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var conversation = (await creation.Content.ReadFromJsonAsync<AssistantConversation>(Json))!;
        var requestId = Guid.NewGuid();
        conversation = await Send(client, conversation.Id, requestId, "Why is this ticket failing?");
        var first = Assert.Single(conversation.Turns);
        Assert.Equal("Completed", first.State); Assert.NotNull(first.Analysis);
        Assert.Equal(fake.Chunk!.ChunkId, Assert.Single(first.Analysis.Sources).ChunkId);
        Assert.All(first.Analysis.RecommendedActions, action => Assert.True(action.RequiresHumanApproval));
        foreach (var name in new[] { "get_ticket", "get_ticket_history", "search_knowledge" })
            Assert.Contains(first.Activity, a => a.Name == name && a.State == "Succeeded");
        var supplied = JsonDocument.Parse(fake.Requests[0].Messages.Last(m => m.Role == "user").Content).RootElement;
        Assert.Equal("Why is this ticket failing?", supplied.GetProperty("task").GetString());
        Assert.Contains("TicketCreated", supplied.GetProperty("ticketHistory").GetRawText());
        var replay = await Send(client, conversation.Id, requestId, "Why is this ticket failing?");
        Assert.Single(replay.Turns); Assert.Equal(2, fake.Requests.Count);
        var again = await Send(client, conversation.Id, Guid.NewGuid(), "Explain the first proposed check.");
        Assert.Equal(2, again.Turns.Count);
        var secondPrompt = JsonDocument.Parse(fake.Requests[2].Messages.Last(m => m.Role == "user").Content).RootElement;
        Assert.Contains("PROPOSED ACTIONS, NOT EXECUTED", secondPrompt.GetProperty("conversationMemory").GetRawText());
        var reload = (await client.GetFromJsonAsync<AssistantConversation>($"/api/assistant/conversations/{conversation.Id}", Json))!;
        Assert.Equal(again.Turns[1].Analysis!.RunId, reload.Turns[1].Analysis!.RunId);
        Assert.Contains((await client.GetFromJsonAsync<AssistantConversationPage>("/api/assistant/conversations", Json))!.Items, c => c.Id == conversation.Id);
        await using var db = fixture.CreateContext();
        Assert.Equal(4, await db.AIMessages.CountAsync(m => m.ConversationId == conversation.Id));
        var invalidCompleted = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.assistant_turns SET analysis_id = NULL WHERE id = {requestId}"));
        Assert.Equal("ck_assistant_turn_state", invalidCompleted.ConstraintName);
        var ticket = await db.Tickets.SingleAsync(t => t.Id == seed.TicketId);
        Assert.Equal(1, ticket.Revision); Assert.Equal(TicketStatus.Open, ticket.Status); Assert.Null(ticket.AssignedTeamId);
        Assert.Equal("no-store", string.Join(',', (await client.GetAsync($"/api/assistant/conversations/{conversation.Id}")).Headers.CacheControl!.ToString().Split(',').Select(v => v.Trim()).Where(v => v == "no-store")));
    }
    [Fact]
    public async Task OwnershipAndTicketScopeCannotBeChosenByTheRequest()
    {
        var seed = await Seed(); await using var factory = Factory(seed, new FakeAi()); using var owner = Client(factory); using var other = Client(factory, OtherToken);
        var conversation = await Create(owner, seed, "Investigate this ticket.");
        using var denied = await other.GetAsync($"/api/assistant/conversations/{conversation.Id}"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<AssistantConversationPage>("/api/assistant/conversations", Json))!.Items);
        using var spoof = await owner.PostAsJsonAsync("/api/assistant/conversations", new { requestId = Guid.NewGuid(), question = "Investigate", ticketId = seed.TicketId, userId = seed.OtherUserId });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var wrongScope = await owner.PostAsJsonAsync("/api/assistant/conversations", new { requestId = Guid.NewGuid(), question = "Investigate", ticketId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, wrongScope.StatusCode);
        using var switchTicket = await owner.PostAsJsonAsync($"/api/assistant/conversations/{conversation.Id}/messages", new { requestId = Guid.NewGuid(), content = "Now investigate ticket #99" });
        Assert.Equal(HttpStatusCode.BadRequest, switchTicket.StatusCode);
    }
    [Theory]
    [InlineData(false, false, 401)]
    [InlineData(true, false, 403)]
    public async Task PrivateAssistantRequiresAuthenticatedUserMapping(bool authenticated, bool mapping, int expected)
    {
        var seed = await Seed(); await using var factory = Factory(seed, new FakeAi(), mapping); using var client = Client(factory, authenticated ? Token : null);
        using var response = await client.GetAsync("/api/assistant/conversations"); Assert.Equal(expected, (int)response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task InactiveUsersCannotReadOrCreateConversations()
    {
        var seed = await Seed(); await using (var db = fixture.CreateContext()) { var user = await db.Users.SingleAsync(u => u.Id == seed.UserId); user.Deactivate(DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        await using var factory = Factory(seed, new FakeAi()); using var client = Client(factory);
        using var response = await client.GetAsync("/api/assistant/conversations"); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task FailedInvestigationRemainsInHistoryWithActualReadsAndDoesNotInventAnAnswer()
    {
        var seed = await Seed(); var fake = new FakeAi { Fail = true }; await using var factory = Factory(seed, fake); await PrepareKnowledge(factory, fake); using var client = Client(factory);
        var conversation = await Create(client, seed, "Investigate the failure.");
        var failed = await Send(client, conversation.Id, Guid.NewGuid(), "Why does the connection fail?");
        var turn = Assert.Single(failed.Turns); Assert.Equal("Failed", turn.State); Assert.Null(turn.Analysis); Assert.Equal("DependencyUnavailable", turn.ErrorCode);
        Assert.Contains(turn.Activity, a => a.Name == "get_ticket_history" && a.State == "Succeeded");
        await using var db = fixture.CreateContext(); Assert.Equal(2, await db.AIMessages.CountAsync(m => m.ConversationId == conversation.Id));
        Assert.Equal(3, await db.TicketPredictions.CountAsync(p => p.TicketId == seed.TicketId));
        var invalidFailure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.assistant_turns SET error_code = NULL WHERE id = {turn.Id}"));
        Assert.Equal("ck_assistant_turn_state", invalidFailure.ConstraintName);
    }
    [Fact]
    public async Task PendingReadsExposeAuditedActivityAndConcurrentOrRepeatedSendsCannotDuplicateWork()
    {
        var seed = await Seed(); var fake = new FakeAi { Hold = true }; await using var factory = Factory(seed, fake); await PrepareKnowledge(factory, fake); using var client = Client(factory);
        var conversation = await Create(client, seed, "Investigate the failure."); var requestId = Guid.NewGuid();
        var running = client.PostAsJsonAsync($"/api/assistant/conversations/{conversation.Id}/messages", new { requestId, content = "Why is it failing?" });
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var observed = (await client.GetFromJsonAsync<AssistantConversation>($"/api/assistant/conversations/{conversation.Id}", Json))!;
        Assert.Equal("Pending", Assert.Single(observed.Turns).State);
        Assert.Contains(observed.Turns[0].Activity, a => a.Name == "get_ticket_history" && a.State == "Succeeded");
        var repeated = await Send(client, conversation.Id, requestId, "Why is it failing?"); Assert.Equal("Pending", Assert.Single(repeated.Turns).State);
        using var conflict = await client.PostAsJsonAsync($"/api/assistant/conversations/{conversation.Id}/messages", new { requestId = Guid.NewGuid(), content = "Another simultaneous message" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        fake.Release.TrySetResult(); using var completed = await running; Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var saved = (await completed.Content.ReadFromJsonAsync<AssistantConversation>(Json))!; Assert.Single(saved.Turns); Assert.Equal("Completed", saved.Turns[0].State); Assert.Equal(2, fake.Requests.Count);
    }
    [Fact]
    public async Task CreateIsIdempotentAndRejectsReusingAnIdentifierForAnotherMessage()
    {
        var seed = await Seed(); await using var factory = Factory(seed, new FakeAi()); using var client = Client(factory); var requestId = Guid.NewGuid();
        var body = new { requestId, question = "Investigate the ticket.", ticketId = seed.TicketId };
        using var first = await client.PostAsJsonAsync("/api/assistant/conversations", body); using var second = await client.PostAsJsonAsync("/api/assistant/conversations", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<AssistantConversation>(Json))!.Id, (await second.Content.ReadFromJsonAsync<AssistantConversation>(Json))!.Id);
        using var conflict = await client.PostAsJsonAsync("/api/assistant/conversations", new { requestId, question = "A different question", ticketId = seed.TicketId }); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }
    private async Task<SeedData> Seed()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1); var user = Guid.NewGuid(); var other = Guid.NewGuid(); var id = Guid.NewGuid(); var reference = "IT-" + Random.Shared.NextInt64(1000000000, 9000000000);
        await using var db = fixture.CreateContext();
        db.Users.AddRange(new User(user, "Assistant owner", EmailAddress.Create(user.ToString("N") + "@example.test"), UserRole.Agent, now), new User(other, "Other owner", EmailAddress.Create(other.ToString("N") + "@example.test"), UserRole.Agent, now));
        db.Tickets.Add(new Ticket(id, reference, "SQL timeout", "Reported SQL Server connection failure", user, now));
        db.AuditLogs.Add(new ITTicketIntelligence.Domain.Auditing.AuditLog(Guid.NewGuid(), user, "TicketCreated", "Ticket", id, now, "Reported observation"));
        await db.SaveChangesAsync(); return new(user, other, id, reference);
    }
    private sealed record SeedData(Guid UserId, Guid OtherUserId, Guid TicketId, string Reference);
    private WebApplicationFactory<Api.Program> Factory(SeedData seed, FakeAi fake, bool mapping = true) => new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<McpSecurityOptions>();
            string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            services.AddSingleton(new McpSecurityOptions([
                new McpCredential(Hash(Token), new McpCaller("assistant-owner", ["tickets.read"], [seed.TicketId]), mapping ? seed.UserId : null),
                new McpCredential(Hash(OtherToken), new McpCaller("assistant-other", ["tickets.read"], [seed.TicketId]), seed.OtherUserId)]));
            services.RemoveAll<ILLMAgentService>(); services.AddSingleton<ILLMAgentService>(fake);
            services.RemoveAll<IRagRetriever>(); services.AddSingleton<IRagRetriever>(fake);
        });
    });
    private static HttpClient Client(WebApplicationFactory<Api.Program> factory, string? token = Token)
    { var client = factory.CreateClient(); if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
    private static async Task<AssistantConversation> Create(HttpClient client, SeedData seed, string question)
    {
        using var response = await client.PostAsJsonAsync("/api/assistant/conversations", new { requestId = Guid.NewGuid(), question, ticketId = seed.TicketId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<AssistantConversation>(Json))!;
    }
    private static async Task<AssistantConversation> Send(HttpClient client, Guid id, Guid requestId, string content)
    {
        using var response = await client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { requestId, content });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<AssistantConversation>(Json))!;
    }
    private static async Task PrepareKnowledge(WebApplicationFactory<Api.Program> factory, FakeAi fake)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateKnowledgeCommand, KnowledgeDocumentDto>>();
        var doc = await handler.HandleAsync(new("SQL assistant guide", "Verify the SQL host and TCP port before proposing changes.", "kb/assistant/" + Guid.NewGuid().ToString("N"), TicketCategory.Database, KnowledgeDocumentKind.Procedure, KnowledgeContentFormat.PlainText));
        var chunk = Assert.Single(doc.Chunks); fake.Chunk = new(doc.Id, doc.Title, chunk.Id, chunk.Content, .9, chunk.Metadata);
    }
    private sealed class FakeAi : IRagRetriever, ILLMAgentService
    {
        public bool Fail { get; init; } public bool Hold { get; init; } public RetrievedChunk? Chunk { get; set; }
        public List<AgentLLMRequest> Requests { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken ct = default) => Task.FromResult(new RetrievedContext("fake", Chunk is null ? [] : [Chunk]));
        public async Task<AgentLLMTurn> CompleteTurnAsync(AgentLLMRequest request, CancellationToken ct = default)
        {
            Requests.Add(request); if (Hold && !request.Finalize) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (Fail) throw new InvalidOperationException("Synthetic private provider error");
            return new([], request.Finalize ? new AgentDraft("Investigate reported SQL connectivity.", "The listener may be unreachable", ["Firewall configuration"], ["Ask an operator to verify TCP 1433"], Chunk is null ? [] : [Chunk.ChunkId.ToString()], .8, Chunk is null) : null, "Fake", "assistant-test", 1);
        }
    }
}
