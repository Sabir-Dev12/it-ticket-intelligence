using System.Text.Json;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Mcp;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Domain.Enums;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Mcp;

public sealed class McpToolServiceTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly McpCaller All = new("test-client", McpToolCatalog.All.Select(t => t.Permission), [Id]);
    private static JsonElement Args(string value) => JsonDocument.Parse(value).RootElement.Clone();
    public static TheoryData<string, string, string> Tools => new()
    {
        { "get_ticket", "{\"ticketId\":\"" + Id + "\"}", "ticket" },
        { "search_tickets", "{\"query\":\"SQL\",\"page\":1,\"pageSize\":2}", "items" },
        { "get_ticket_history", "{\"ticketId\":\"" + Id + "\"}", "entries" },
        { "search_knowledge", "{\"query\":\"SQL timeout\",\"limit\":2}", "results" },
        { "get_team", "{\"name\":\"Database\"}", "team" },
        { "get_system_status", "{}", "database" }
    };

    [Theory, MemberData(nameof(Tools))]
    public async Task EachToolHasAValidatedReadContractAndDurableAudit(string name, string input, string output)
    {
        var f = new Fake(); var result = await f.Service.CallAsync(name, Args(input), All);
        Assert.False(result.IsError); Assert.True(result.Content.TryGetProperty(output, out _));
        var definition = Assert.Single(f.Service.List(All), t => t.Name == name);
        Assert.Equal("object", definition.InputSchema.GetProperty("type").GetString());
        Assert.Equal("object", definition.OutputSchema.GetProperty("type").GetString());
        Assert.False(definition.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "Started", "Succeeded" }, f.Events.Select(e => e.Outcome));
        Assert.All(f.Events, e => { Assert.Equal(f.Events[0].CallId, e.CallId); Assert.Equal("test-client", e.Subject); Assert.Equal(name, e.Tool); Assert.Equal(64, e.ArgumentsHash.Length); });
        Assert.NotNull(f.Events[1].OutputHash);
    }

    [Theory, MemberData(nameof(Tools))]
    public async Task EveryToolDeniesMissingPermissionWithoutReadingData(string name, string input, string _)
    {
        var f = new Fake(); var caller = new McpCaller("no-permissions", [], [Id]);
        var result = await f.Service.CallAsync(name, Args(input), caller);
        Assert.True(result.IsError); Assert.Equal("Forbidden", Error(result)); Assert.Empty(f.Calls);
        Assert.Equal("Denied", f.Events.Last().Outcome); Assert.Empty(f.Service.List(caller));
    }

    [Theory]
    [InlineData("get_ticket", "{}")]
    [InlineData("get_ticket", "{\"ticketId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("get_ticket", "{\"ticketId\":123}")]
    [InlineData("get_ticket", "[]")]
    [InlineData("get_ticket_history", "{\"ticketId\":\"not-a-guid\"}")]
    [InlineData("search_tickets", "{\"query\":\"SQL\",\"pageSize\":21}")]
    [InlineData("search_tickets", "{\"query\":\"SQL\",\"page\":0}")]
    [InlineData("search_tickets", "{\"query\":\"SQL\",\"page\":1.5}")]
    [InlineData("search_tickets", "{\"query\":\"SQL\",\"page\":\"1\"}")]
    [InlineData("search_tickets", "{\"query\":\"SQL\",\"sql\":\"DROP TABLE tickets\"}")]
    [InlineData("search_knowledge", "{\"query\":\" \"}")]
    [InlineData("search_knowledge", "{\"query\":\"a\",\"query\":\"b\"}")]
    [InlineData("search_knowledge", "{\"query\":\"SQL\",\"limit\":6}")]
    [InlineData("get_team", "{\"name\":\"Unknown\"}")]
    [InlineData("get_system_status", "{\"connectionString\":\"some-secret\"}")]
    public async Task RejectsArgumentsOutsideThePublishedSchema(string name, string input)
    {
        var f = new Fake(); var result = await f.Service.CallAsync(name, Args(input), All);
        Assert.True(result.IsError); Assert.Equal("InvalidArguments", Error(result)); Assert.Empty(f.Calls);
        Assert.Equal("Rejected", f.Events.Last().Outcome);
        Assert.DoesNotContain("some-secret", JsonSerializer.Serialize(f.Events));
        Assert.DoesNotContain("DROP TABLE", result.Content.GetRawText());
    }

    [Fact]
    public async Task TicketScopeCannotBeOverriddenAndSearchReceivesTheTrustedScope()
    {
        var f = new Fake();
        foreach (var name in new[] { "get_ticket", "get_ticket_history" })
        {
            var response = await f.Service.CallAsync(name, JsonSerializer.SerializeToElement(new { ticketId = Guid.NewGuid() }), All);
            Assert.Equal("Forbidden", Error(response));
        }
        Assert.Empty(f.Calls);
        await f.Service.CallAsync("search_tickets", Args("{\"query\":\"\"}"), All);
        Assert.Same(All, f.SearchScope); Assert.False(f.SearchScope!.AllTickets); Assert.Single(f.SearchScope.TicketIds);
        Assert.Single(f.Service.List(new McpCaller("one-permission", ["teams.read"])));
    }

    [Fact]
    public async Task UnknownToolsNeverReachPersistence()
    {
        var f = new Fake(); var result = await f.Service.CallAsync("execute_sql", Args("{\"sql\":\"secret\"}"), All);
        Assert.Equal("UnknownTool", Error(result)); Assert.Empty(f.Calls);
        Assert.All(f.Events, e => Assert.Equal("unrecognized", e.Tool));
    }

    [Fact]
    public async Task MissingTicketsAndNullTeamsHaveDifferentDocumentedResults()
    {
        var f = new Fake { Missing = true };
        var missing = await f.Service.CallAsync("get_ticket", JsonSerializer.SerializeToElement(new { ticketId = Id }), All);
        Assert.Equal("NotFound", Error(missing));
        var team = await f.Service.CallAsync("get_team", Args("{\"name\":\"Support\"}"), All);
        Assert.False(team.IsError); Assert.Equal(JsonValueKind.Null, team.Content.GetProperty("team").ValueKind);
    }

    [Fact]
    public async Task AuditFailurePreventsReadAndProviderFailureIsRedacted()
    {
        var f = new Fake { FailAudit = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CallAsync("get_system_status", Args("{}"), All));
        Assert.Empty(f.Calls);
        var g = new Fake { FailRead = true };
        var result = await g.Service.CallAsync("get_system_status", Args("{}"), All);
        Assert.Equal("Unavailable", Error(result)); Assert.DoesNotContain("raw-secret", result.Content.GetRawText());
        Assert.Equal("Failed", g.Events.Last().Outcome);
    }

    [Fact]
    public async Task CancellationStopsReadsAndPreservesTerminalAudit()
    {
        using var cts = new CancellationTokenSource(); var f = new Fake { OnRead = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.CallAsync("get_system_status", Args("{}"), All, cts.Token));
        Assert.Equal("Cancelled", f.Events.Last().Outcome);
        var g = new Fake();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.Service.CallAsync("get_system_status", Args("{}"), All, cts.Token));
        Assert.Empty(g.Events);
    }

    [Fact]
    public async Task AgentAdapterCannotReadAnotherTicketEvenWhenTheModelSuppliesItsId()
    {
        var f = new Fake(); var adapter = new McpAgentTools(f.Service);
        await Assert.ThrowsAsync<AgentToolDeniedException>(() => adapter.ExecuteAsync(Id,
            new("call", "get_ticket", JsonSerializer.Serialize(new { ticketId = Guid.NewGuid() })), default));
        Assert.Empty(f.Calls);
        var own = await adapter.ExecuteAsync(Id, new("host", "get_ticket", "{}"), default);
        Assert.Equal(Id, own.Ticket!.Id);
    }

    private static string? Error(McpToolResponse response) => response.Content.GetProperty("error").GetProperty("code").GetString();
    private sealed class Fake : IMcpReadStore, IRagRetriever, IMcpAuditWriter
    {
        public McpToolService Service => new(this, this, this, TimeProvider.System);
        public List<McpAuditEntry> Events { get; } = [];
        public List<string> Calls { get; } = [];
        public bool FailAudit { get; init; }
        public bool FailRead { get; init; }
        public bool Missing { get; init; }
        public Action? OnRead { get; init; }
        public McpCaller? SearchScope { get; private set; }
        private void Read(string name, CancellationToken ct) { Calls.Add(name); OnRead?.Invoke(); ct.ThrowIfCancellationRequested(); if (FailRead) throw new InvalidOperationException("raw-secret"); }
        public Task WriteAsync(McpAuditEntry entry, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (FailAudit) throw new InvalidOperationException(); Events.Add(entry); return Task.CompletedTask; }
        public Task<AgentTicketFacts?> GetTicketAsync(Guid id, CancellationToken ct) { Read("ticket", ct); return Task.FromResult<AgentTicketFacts?>(Missing ? null : new(id, 1, "SQL timeout", "Reported timeout", TicketStatus.Open, false, null)); }
        public Task<McpSearchOutput> SearchAsync(string query, int page, int pageSize, McpCaller scope, CancellationToken ct)
        { Read("search", ct); SearchScope = scope; return Task.FromResult(new McpSearchOutput([], page, pageSize, false)); }
        public Task<IReadOnlyList<AgentHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken ct) { Read("history", ct); return Task.FromResult<IReadOnlyList<AgentHistoryEntry>>([]); }
        public Task<AgentTeamInformation?> GetTeamAsync(string name, CancellationToken ct) { Read("team", ct); return Task.FromResult<AgentTeamInformation?>(null); }
        public Task<McpStatusOutput> GetStatusAsync(CancellationToken ct) { Read("status", ct); return Task.FromResult(new McpStatusOutput("ITTicketIntelligence", "Available", DateTimeOffset.UtcNow, true)); }
        public Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default) { Read("rag", cancellationToken); return Task.FromResult(new RetrievedContext("fake", [])); }
    }
}
