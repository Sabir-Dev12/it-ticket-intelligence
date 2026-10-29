using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Application.Mcp;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
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

namespace ITTicketIntelligence.IntegrationTests.Mcp;

[Collection("Postgres")]
public sealed class McpServerTests(PostgresFixture fixture)
{
    private const string Token = "mcp-test-only-token-0123456789abcdef0123456789";
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));

    [Theory]
    [InlineData("get_ticket")]
    [InlineData("search_tickets")]
    [InlineData("get_ticket_history")]
    [InlineData("search_knowledge")]
    [InlineData("get_team")]
    [InlineData("get_system_status")]
    public async Task EachToolRunsOverTheRealMcpTransportWithAuthorizationAndAudit(string name)
    {
        var (own, other) = await Seed();
        await using var factory = Factory(new McpCaller("mcp-test-reader", McpToolCatalog.All.Select(t => t.Permission), [own.Id]));
        using var client = Client(factory);
        var initialized = await Rpc(client, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "xunit", version = "1" } });
        Assert.True(initialized.GetProperty("result").TryGetProperty("capabilities", out _));
        var definition = await Rpc(client, "tools/list", new { });
        var tools = definition.GetProperty("result").GetProperty("tools"); Assert.Equal(6, tools.GetArrayLength());
        foreach (var tool in tools.EnumerateArray())
        {
            Assert.True(tool.TryGetProperty("outputSchema", out _));
            Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            Assert.False(tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
            Assert.True(tool.GetProperty("_meta").TryGetProperty("requiredPermission", out _));
        }
        object args = name switch
        {
            "get_ticket" or "get_ticket_history" => new { ticketId = own.Id },
            "search_tickets" => new { query = "MCP SQL", page = 1, pageSize = 1 },
            "search_knowledge" => new { query = "SQL timeout", limit = 2 },
            "get_team" => new { name = "Database" }, _ => new { }
        };
        var response = (await Rpc(client, "tools/call", new { name, arguments = args })).GetProperty("result");
        Assert.False(response.TryGetProperty("isError", out var error) && error.GetBoolean(), response.GetRawText());
        var content = response.GetProperty("structuredContent");
        switch (name)
        {
            case "get_ticket": Assert.Equal(own.Id, content.GetProperty("ticket").GetProperty("id").GetGuid()); break;
            case "search_tickets": Assert.Equal(own.Id, Assert.Single(content.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()); Assert.False(content.GetProperty("hasMore").GetBoolean()); break;
            case "get_ticket_history": Assert.Equal(own.Id, content.GetProperty("ticketId").GetGuid()); Assert.NotEmpty(content.GetProperty("entries").EnumerateArray()); break;
            case "search_knowledge": Assert.Equal("kb/mcp-test", Assert.Single(content.GetProperty("results").EnumerateArray()).GetProperty("metadata").GetProperty("source").GetString()); break;
            case "get_team": Assert.True(content.TryGetProperty("team", out _)); break;
            case "get_system_status": Assert.Equal("Available", content.GetProperty("database").GetString()); Assert.DoesNotContain("Password", content.GetRawText()); break;
        }
        await using var db = fixture.CreateContext();
        var audits = await db.AuditLogs.Where(a => a.EntityType == "McpToolCall" && a.Details!.Contains("mcp-test-reader")).ToListAsync();
        Assert.Contains(audits, a => a.Action == "Mcp.Tool.Succeeded" && a.Details!.Contains(name));
        Assert.DoesNotContain(audits, a => a.Details!.Contains(Token));
        var unchanged = await db.Tickets.SingleAsync(t => t.Id == own.Id);
        Assert.Equal(own.Revision, unchanged.Revision); Assert.Null(unchanged.AssignedTeamId); Assert.Null(unchanged.Priority);
        Assert.False(await db.TicketPredictions.AnyAsync(p => p.TicketId == own.Id || p.TicketId == other.Id));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.IsType<McpAgentTools>(scope.ServiceProvider.GetRequiredService<ITicketAgentTools>());
    }

    [Theory]
    [InlineData("get_ticket")]
    [InlineData("search_tickets")]
    [InlineData("get_ticket_history")]
    [InlineData("search_knowledge")]
    [InlineData("get_team")]
    [InlineData("get_system_status")]
    public async Task PermissionCannotBeEscalatedThroughArguments(string name)
    {
        await using var factory = Factory(new McpCaller("denied-client", [])); using var client = Client(factory);
        var list = await Rpc(client, "tools/list", new { }); Assert.Empty(list.GetProperty("result").GetProperty("tools").EnumerateArray());
        var response = (await Rpc(client, "tools/call", new { name, arguments = new { permissions = new[] { "tickets.read" }, allTickets = true } })).GetProperty("result");
        Assert.True(response.GetProperty("isError").GetBoolean());
        Assert.Equal("Forbidden", response.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScopeAppliesBeforeLookupAndPaginationAndSearchIsLiteral()
    {
        var (own, other) = await Seed();
        await using var factory = Factory(new("scope-client", ["tickets.read", "tickets.search", "tickets.history.read"], [own.Id])); using var client = Client(factory);
        foreach (var name in new[] { "get_ticket", "get_ticket_history" })
        {
            var result = (await Rpc(client, "tools/call", new { name, arguments = new { ticketId = other.Id } })).GetProperty("result");
            Assert.Equal("Forbidden", result.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain(other.Title, result.GetRawText());
        }
        var search = (await Rpc(client, "tools/call", new { name = "search_tickets", arguments = new { query = "MCP SQL", pageSize = 20 } })).GetProperty("result").GetProperty("structuredContent");
        Assert.Equal(own.Id, Assert.Single(search.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var literal = (await Rpc(client, "tools/call", new { name = "search_tickets", arguments = new { query = "' OR 1=1 --" } })).GetProperty("result").GetProperty("structuredContent");
        Assert.Empty(literal.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-credential-0123456789abcdef0123456789")]
    public async Task MissingOrInvalidBearerReturns401(string? token)
    {
        await using var factory = Factory(new("auth-client", ["system.status.read"])); using var client = Client(factory, token);
        using var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task NoConfiguredClientsMeansTheServerIsClosed()
    {
        await using var factory = Factory(new("unused-client", ["system.status.read"]), configured: false);
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("host")]
    [InlineData("duplicate")]
    [InlineData("large")]
    public async Task RejectsUnsafeHttpRequestsBeforeToolDispatch(string kind)
    {
        await using var factory = Factory(new("guard-client", ["system.status.read"])); using var client = Client(factory);
        if (kind == "origin") client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        if (kind == "host") client.DefaultRequestHeaders.Host = "attacker.example";
        var body = kind == "duplicate" ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"get_system_status\",\"arguments\":{\"sql\":1,\"sql\":2}}}"
            : kind == "large" ? new string('a', 17000) : "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}";
        using var response = await client.PostAsync("/mcp", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(kind == "large" ? HttpStatusCode.RequestEntityTooLarge : kind == "duplicate" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownToolsAndExtraSqlParametersReturnControlledErrors()
    {
        await using var factory = Factory(new("validation-client", ["system.status.read"])); using var client = Client(factory);
        foreach (var (name, code) in new[] { ("execute_sql", "UnknownTool"), ("get_system_status", "InvalidArguments") })
        {
            var response = (await Rpc(client, "tools/call", new { name, arguments = new { sql = "DROP TABLE app.tickets" } })).GetProperty("result");
            Assert.True(response.GetProperty("isError").GetBoolean());
            Assert.Equal(code, response.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain("DROP TABLE", response.GetRawText());
        }
    }

    [Fact]
    public async Task CurrentStatelessProtocolWorksWithoutAnInitializeSession()
    {
        await using var factory = Factory(new("current-protocol", ["system.status.read"])); using var client = Client(factory);
        client.DefaultRequestHeaders.Remove("MCP-Protocol-Version"); client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-07-28");
        var result = await Rpc(client, "tools/call", new { name = "get_system_status", arguments = new { } });
        Assert.Equal("Available", result.GetProperty("result").GetProperty("structuredContent").GetProperty("database").GetString());
    }

    [Fact]
    public void ConfigurationUsesEnvironmentAndNeverSerializesCredentials()
    {
        var options = McpSecurityOptions.FromEnvironment(name => name switch
        { "MCP__Clients__0__TokenHash" => Hash, "MCP__Clients__0__Subject" => "configured-client", "MCP__Clients__0__Permissions" => "teams.read", _ => null });
        Assert.Single(options.Clients); Assert.DoesNotContain(Hash, JsonSerializer.Serialize(options)); Assert.DoesNotContain(Hash, options.ToString()!);
        Assert.Empty(McpSecurityOptions.FromEnvironment(_ => null).Clients);
        Assert.Throws<ArgumentException>(() => McpSecurityOptions.FromEnvironment(name => name switch
        { "MCP__Clients__0__TokenHash" => "bad-sensitive-value", _ => null }));
    }

    private WebApplicationFactory<Api.Program> Factory(McpCaller caller, bool configured = true) => new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<McpSecurityOptions>(); services.AddSingleton(new McpSecurityOptions(configured ? [new McpCredential(Hash, caller)] : []));
            services.RemoveAll<IRagRetriever>(); services.AddSingleton<IRagRetriever, FakeRag>();
        });
    });
    private static HttpClient Client(WebApplicationFactory<Api.Program> factory, string? token = Token)
    {
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Accept.Add(new("application/json")); client.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
        client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static async Task<JsonElement> Rpc(HttpClient client, string method, object args)
    {
        var parameters = JsonSerializer.SerializeToNode(args)!.AsObject();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        if (client.DefaultRequestHeaders.GetValues("MCP-Protocol-Version").Contains("2026-07-28"))
        {
            parameters["_meta"] = new System.Text.Json.Nodes.JsonObject { ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                ["io.modelcontextprotocol/clientCapabilities"] = new System.Text.Json.Nodes.JsonObject() };
            request.Headers.Add("Mcp-Method", method);
            if (parameters["name"] is { } name) request.Headers.Add("Mcp-Name", name.GetValue<string>());
        }
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync(); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream") raw = raw.Split('\n').Last(s => s.StartsWith("data: ", StringComparison.Ordinal))[6..];
        using var doc = JsonDocument.Parse(raw); Assert.False(doc.RootElement.TryGetProperty("error", out _), raw);
        return doc.RootElement.Clone();
    }
    private async Task<(Ticket Own, Ticket Other)> Seed()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1); var key = Guid.NewGuid();
        var user = new User(Guid.NewGuid(), "MCP tests", EmailAddress.Create(key.ToString("N") + "@example.com"), UserRole.User, now);
        Ticket Create(string suffix) { var id = Guid.NewGuid(); return new Ticket(id, "MC-" + id.ToString("N")[..20], "MCP SQL " + suffix, "Reported connection timeout.", user.Id, now); }
        var own = Create("own"); var other = Create("private");
        own.AddComment(new TicketComment(Guid.NewGuid(), own.Id, user.Id, "A reported connection error", now));
        await using var db = fixture.CreateContext(); db.Users.Add(user); db.Tickets.AddRange(own, other); await db.SaveChangesAsync(); return (own, other);
    }
    private sealed class FakeRag : IRagRetriever
    {
        public Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default)
        {
            var document = Guid.NewGuid(); var metadata = new KnowledgeChunkMetadata(document, 1, "SQL guide", "kb/mcp-test", TicketCategory.Database,
                KnowledgeDocumentKind.Procedure, KnowledgeContentFormat.Markdown, 0, 0, 20, new string('a', 64));
            return Task.FromResult(new RetrievedContext("fake", [new(document, "SQL guide", Guid.NewGuid(), "Verify connectivity.", .9, metadata)]));
        }
    }
}
