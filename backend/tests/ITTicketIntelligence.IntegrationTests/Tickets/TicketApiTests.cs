using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Tickets;

[Collection("Postgres")]
public sealed class TicketApiTests : IDisposable
{
    private readonly PostgresFixture fixture;
    private readonly TicketApiFactory factory;
    private readonly HttpClient client;
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public TicketApiTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
        factory = new TicketApiFactory(fixture.ConnectionString);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task WorkspaceReadsAssignedTeamAndOnlyActiveChoicesWithoutRunningAnAnalysis()
    {
        var (user, team) = await References();
        var ticket = await Create(user.Id, "Workspace lookup");
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = team.Id, revision = ticket.Revision });
        var before = await Success<TicketWorkspaceDto>(HttpMethod.Get, $"/api/tickets/{ticket.Id}/workspace");
        Assert.Equal(team.Name, before.AssignedTeam?.Name);
        Assert.Contains(before.AssignableTeams, t => t.Id == team.Id);
        Assert.Null(before.Analysis);
        await using (var db = fixture.CreateContext())
        {
            var stored = await db.Teams.SingleAsync(t => t.Id == team.Id);
            stored.Deactivate(DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        }
        var after = await Success<TicketWorkspaceDto>(HttpMethod.Get, $"/api/tickets/{ticket.Id}/workspace");
        Assert.False(after.AssignedTeam!.IsActive);
        Assert.DoesNotContain(after.AssignableTeams, t => t.Id == team.Id);
        var current = await Success<TicketDto>(HttpMethod.Get, $"/api/tickets/{ticket.Id}");
        Assert.Equal(ticket.Revision, current.Revision); Assert.Empty(current.Predictions);
    }

    [Fact]
    public async Task WorkspaceReturnsProblemDetailsForMissingAndEmptyTicketIds()
    {
        using var missing = await Send(HttpMethod.Get, $"/api/tickets/{Guid.NewGuid()}/workspace");
        await Problem(missing, HttpStatusCode.NotFound);
        using var empty = await Send(HttpMethod.Get, $"/api/tickets/{Guid.Empty}/workspace");
        await Problem(empty, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FullLifecycle_UsesAllEndpoints_AndPreservesCommentsAndAudit()
    {
        var (user, team) = await References();
        using var creation = await Send(HttpMethod.Post, "/api/tickets", new { title = "Database unreachable", description = "The application cannot connect to its database.", createdById = user.Id });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var ticket = await Read<TicketDto>(creation);
        Assert.EndsWith("/api/tickets/" + ticket.Id, creation.Headers.Location?.ToString());
        Assert.Matches("^IT-[0-9]+$", ticket.Reference);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(1, ticket.Revision);

        ticket = await Success<TicketDto>(HttpMethod.Get, "/api/tickets/" + ticket.Id);
        ticket = await Success<TicketDto>(HttpMethod.Put, "/api/tickets/" + ticket.Id, new { title = "Updated incident", description = "Investigation details.", category = "Database", priority = "High", revision = ticket.Revision });
        Assert.Equal(TicketCategory.Database, ticket.Category);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = team.Id, revision = ticket.Revision });
        Assert.Equal(team.Id, ticket.AssignedTeamId);
        var comment = await Success<AddCommentResult>(HttpMethod.Post, $"/api/tickets/{ticket.Id}/comments", new { authorId = user.Id, message = "Connection logs collected.", revision = ticket.Revision }, HttpStatusCode.Created);
        Assert.Equal(ticket.Revision + 1, comment.TicketRevision);
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "InProgress", revision = comment.TicketRevision });
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "Resolved", resolution = "The endpoint was corrected.", revision = ticket.Revision });
        Assert.NotNull(ticket.ResolvedAt);
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "Closed", revision = ticket.Revision });
        Assert.NotNull(ticket.ClosedAt);

        using var archive = await Send(HttpMethod.Delete, $"/api/tickets/{ticket.Id}?revision={ticket.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        ticket = await Success<TicketDto>(HttpMethod.Get, "/api/tickets/" + ticket.Id);
        Assert.True(ticket.IsArchived);
        Assert.Single(ticket.Comments);
        var page = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}");
        Assert.Empty(page.Items);
        page = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&includeArchived=true");
        Assert.Single(page.Items);
        ticket = await Success<TicketDto>(HttpMethod.Post, $"/api/tickets/{ticket.Id}/restore", new { revision = ticket.Revision });
        Assert.False(ticket.IsArchived);
        Assert.Equal(TicketStatus.Closed, ticket.Status);

        await using var db = fixture.CreateContext();
        Assert.Equal(9, await db.AuditLogs.CountAsync(log => log.EntityId == ticket.Id));
        Assert.True(await db.Tickets.AnyAsync(t => t.Id == ticket.Id));
        Assert.Equal("Connection logs collected.", (await db.TicketComments.SingleAsync(c => c.Id == comment.Comment.Id)).Message);
    }

    [Fact]
    public async Task List_SupportsPaginationFiltersAndLiteralCaseInsensitiveSearch()
    {
        var (user, team) = await References();
        var first = await Create(user.Id, "VPN 100%_literal failure");
        await Create(user.Id, "VPN ordinary failure");
        await Create(user.Id, "Email incident");
        first = await Success<TicketDto>(HttpMethod.Put, $"/api/tickets/{first.Id}", new { title = first.Title, description = first.Description, category = "Network", priority = "High", revision = first.Revision });
        await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{first.Id}/assignment", new { teamId = team.Id, revision = first.Revision });
        var page1 = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&page=1&pageSize=1");
        var page2 = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&page=2&pageSize=1");
        Assert.Equal(3, page1.TotalCount);
        Assert.Single(page1.Items);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
        var filtered = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&category=Network&priority=High&status=Open&assignedTeamId={team.Id}&search=vPn");
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        var literal = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&search=%25_");
        Assert.Equal(first.Id, Assert.Single(literal.Items).Id);
        var empty = await Success<PagedResult<TicketSummaryDto>>(HttpMethod.Get, $"/api/tickets?createdById={user.Id}&page=999");
        Assert.Empty(empty.Items);
        Assert.Equal(3, empty.TotalCount);
    }

    [Fact]
    public async Task Put_CanClearClassification_AndAssignmentCanBeRemoved()
    {
        var (user, team) = await References();
        var ticket = await Create(user.Id);
        ticket = await Success<TicketDto>(HttpMethod.Put, $"/api/tickets/{ticket.Id}", new { title = "Classified", description = "Updated description", category = "Network", priority = "High", revision = ticket.Revision });
        ticket = await Success<TicketDto>(HttpMethod.Put, $"/api/tickets/{ticket.Id}", new { title = "Unclassified", description = "Updated description", category = (string?)null, priority = (string?)null, revision = ticket.Revision });
        Assert.Null(ticket.Category);
        Assert.Null(ticket.Priority);
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = team.Id, revision = ticket.Revision });
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = (Guid?)null, revision = ticket.Revision });
        Assert.Null(ticket.AssignedTeamId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"createdById\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"title\":\" \",\"description\":\"Description\",\"createdById\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{malformed")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"createdById\":\"00000000-0000-0000-0000-000000000001\",\"isArchived\":true}")]
    public async Task InvalidCreateBodies_ReturnValidationProblemDetails(string json)
    {
        using var response = await client.PostAsync("/api/tickets", new StringContent(json, Encoding.UTF8, "application/json"));
        var problem = await Problem(response, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    [InlineData("priority=Unknown")]
    [InlineData("assignedTeamId=00000000-0000-0000-0000-000000000000")]
    public async Task InvalidListFilters_Return400(string query)
    {
        using var response = await Send(HttpMethod.Get, "/api/tickets?" + query);
        await Problem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownResources_Return404_AndInactiveReferencesReturn409()
    {
        using (var response = await Send(HttpMethod.Get, "/api/tickets/" + Guid.NewGuid())) await Problem(response, HttpStatusCode.NotFound);
        using (var response = await Send(HttpMethod.Post, "/api/tickets", new { title = "Title", description = "Description", createdById = Guid.NewGuid() })) await Problem(response, HttpStatusCode.NotFound);
        var (inactive, inactiveTeam) = await References(active: false);
        using (var response = await Send(HttpMethod.Post, "/api/tickets", new { title = "Title", description = "Description", createdById = inactive.Id })) await Problem(response, HttpStatusCode.Conflict);
        var (user, _) = await References();
        var ticket = await Create(user.Id);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = Guid.NewGuid(), revision = ticket.Revision })) await Problem(response, HttpStatusCode.NotFound);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/assignment", new { teamId = inactiveTeam.Id, revision = ticket.Revision })) await Problem(response, HttpStatusCode.Conflict);
        using (var response = await Send(HttpMethod.Post, $"/api/tickets/{ticket.Id}/comments", new { authorId = inactive.Id, message = "Message", revision = ticket.Revision })) await Problem(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StatusValidation_SeparatesInvalidPayloadFromInvalidTransition()
    {
        var (user, _) = await References();
        var ticket = await Create(user.Id);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "Closed", revision = 1 })) await Problem(response, HttpStatusCode.Conflict);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "Resolved", revision = 1 })) await Problem(response, HttpStatusCode.BadRequest);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = 1, revision = 1 })) await Problem(response, HttpStatusCode.BadRequest);
        using (var response = await Send(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { revision = 1 })) await Problem(response, HttpStatusCode.BadRequest);
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "InProgress", revision = ticket.Revision });
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "Resolved", resolution = "Service restored.", revision = ticket.Revision });
        ticket = await Success<TicketDto>(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status", new { status = "InProgress", revision = ticket.Revision });
        Assert.Null(ticket.Resolution);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public async Task ConcurrentUpdates_AllowOnlyOneWriterAndOneAdditionalAudit()
    {
        var (user, _) = await References();
        var ticket = await Create(user.Id);
        var responses = await Task.WhenAll(Send(HttpMethod.Put, $"/api/tickets/{ticket.Id}", new { title = "Writer one", description = ticket.Description, revision = 1 }),
            Send(HttpMethod.Put, $"/api/tickets/{ticket.Id}", new { title = "Writer two", description = ticket.Description, revision = 1 }));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await Problem(conflict, HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var current = await Success<TicketDto>(HttpMethod.Get, $"/api/tickets/{ticket.Id}");
        Assert.Equal(2, current.Revision);
        await using var db = fixture.CreateContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(log => log.EntityId == ticket.Id));
    }

    [Fact]
    public async Task Archive_RejectsFurtherEdits_AndDoesNotDuplicateNoOpAudits()
    {
        var (user, _) = await References();
        var ticket = await Create(user.Id);
        using (var response = await Send(HttpMethod.Delete, $"/api/tickets/{ticket.Id}")) await Problem(response, HttpStatusCode.BadRequest);
        using (var response = await Send(HttpMethod.Delete, $"/api/tickets/{ticket.Id}?revision=1")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ticket = await Success<TicketDto>(HttpMethod.Get, $"/api/tickets/{ticket.Id}");
        using (var response = await Send(HttpMethod.Put, $"/api/tickets/{ticket.Id}", new { title = "Changed", description = "Changed description", revision = ticket.Revision })) await Problem(response, HttpStatusCode.Conflict);
        using (var response = await Send(HttpMethod.Post, $"/api/tickets/{ticket.Id}/comments", new { authorId = user.Id, message = "Message", revision = ticket.Revision })) await Problem(response, HttpStatusCode.Conflict);
        using (var response = await Send(HttpMethod.Delete, $"/api/tickets/{ticket.Id}?revision={ticket.Revision}")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(log => log.EntityId == ticket.Id));
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task GlobalErrors_HideInternalDetails_AndDoNotPersistUnsavedTickets(bool unavailable, HttpStatusCode status)
    {
        var (user, _) = await References();
        await using var failingFactory = new TicketApiFactory(fixture.ConnectionString, services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(_ => new FailingUnitOfWork(unavailable));
        });
        using var failingClient = failingFactory.CreateClient();
        var title = "Failure " + Guid.NewGuid();
        using var response = await failingClient.PostAsJsonAsync("/api/tickets", new { title, description = "Description", createdById = user.Id });
        var problem = await Problem(response, status);
        Assert.DoesNotContain("internal-diagnostic", problem.ToString());
        await using var db = fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync(ticket => ticket.Title == title));
    }

    [Fact]
    public async Task TicketAndAudit_RollBackTogether_WhenTheDatabaseRejectsTheAudit()
    {
        var (user, _) = await References();
        await using var failingFactory = new TicketApiFactory(fixture.ConnectionString, services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddScoped<IAuditLogWriter, InvalidActorAuditWriter>();
        });
        using var failingClient = failingFactory.CreateClient();
        var title = "Rollback " + Guid.NewGuid();
        using var response = await failingClient.PostAsJsonAsync("/api/tickets", new { title, description = "Description", createdById = user.Id });
        await Problem(response, HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync(ticket => ticket.Title == title));
    }

    [Fact]
    public async Task OpenApi_DescribesAllTicketOperations()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await Read<JsonElement>(response);
        var paths = document.GetProperty("paths");
        foreach (var (path, method) in new[] { ("/api/tickets", "post"), ("/api/tickets", "get"),
            ("/api/tickets/{id}", "get"), ("/api/tickets/{id}", "put"), ("/api/tickets/{id}", "delete"),
            ("/api/tickets/{id}/status", "patch"), ("/api/tickets/{id}/assignment", "patch"),
            ("/api/tickets/{id}/comments", "post"), ("/api/tickets/{id}/restore", "post") })
            Assert.True(paths.GetProperty(path).TryGetProperty(method, out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "TicketStatus", "TicketPriority", "TicketCategory", "PredictionType" })
        {
            var schema = schemas.GetProperty(name);
            var values = schema.GetProperty("enum").EnumerateArray().ToArray();
            Assert.NotEmpty(values);
            Assert.All(values, value => Assert.True(value.ValueKind is JsonValueKind.String or JsonValueKind.Null));
            var expected = name switch
            {
                "TicketStatus" => Enum.GetNames<TicketStatus>(),
                "TicketPriority" => Enum.GetNames<TicketPriority>(),
                "TicketCategory" => Enum.GetNames<TicketCategory>(),
                _ => Enum.GetNames<PredictionType>()
            };
            Assert.Equal(expected, values.Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()));
        }
    }

    private async Task<TicketDto> Create(Guid userId, string title = "Database incident") =>
        await Success<TicketDto>(HttpMethod.Post, "/api/tickets", new { title, description = "The application cannot connect to its database.", createdById = userId }, HttpStatusCode.Created);

    private async Task<(User User, Team Team)> References(bool active = true)
    {
        await using var db = fixture.CreateContext();
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var user = new User(Guid.NewGuid(), "API test user", EmailAddress.Create(Guid.NewGuid().ToString("N") + "@example.com"), UserRole.Agent, now);
        var team = new Team(Guid.NewGuid(), "API team " + Guid.NewGuid().ToString("N"), now);
        if (!active) { user.Deactivate(now); team.Deactivate(now); }
        db.Users.Add(user);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return (user, team);
    }

    private async Task<T> Success<T>(HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await Send(method, path, body);
        Assert.True(response.StatusCode == expected, $"Expected {expected}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Read<T>(response);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await Read<JsonElement>(response);
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("instance").GetString()));
        return problem;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public void Dispose() { client.Dispose(); factory.Dispose(); }

    private sealed class TicketApiFactory(string connection, Action<IServiceCollection>? customize = null) : WebApplicationFactory<Api.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connection }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            if (customize is not null) builder.ConfigureServices(customize);
        }
    }

    private sealed class FailingUnitOfWork(bool unavailable) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromException<int>(unavailable
            ? new NpgsqlException("internal-diagnostic") : new InvalidOperationException("internal-diagnostic"));
    }

    private sealed class InvalidActorAuditWriter(AppDbContext db) : IAuditLogWriter
    {
        public void Add(AuditLog audit) => db.AuditLogs.Add(new AuditLog(audit.Id, Guid.NewGuid(), audit.Action,
            audit.EntityType, audit.EntityId, audit.OccurredAt));
    }
}
