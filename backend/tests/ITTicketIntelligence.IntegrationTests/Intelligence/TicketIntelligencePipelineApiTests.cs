using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Intelligence;
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
using Npgsql;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

[Collection("Postgres")]
public sealed class TicketIntelligencePipelineApiTests(PostgresFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(.94, .88, false)]
    [InlineData(.94, .4, true)]
    [InlineData(.4, .88, true)]
    public async Task EndpointAppendsThreePredictionsWithoutApplyingThem(double categoryScore, double teamScore, bool review)
    {
        var ticket = await Seed();
        var predictors = new Predictors { CategoryScore = categoryScore, TeamScore = teamScore };
        await using var factory = Factory(predictors);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IntelligenceResult>(Json);
        Assert.NotNull(result);
        Assert.Equal(TicketCategory.Database, result.Category);
        Assert.Equal(TicketPriority.High, result.Priority);
        Assert.Equal(categoryScore, result.CategoryConfidence);
        Assert.Equal(.91, result.PriorityConfidence);
        Assert.Equal(teamScore, result.TeamConfidence);
        Assert.Equal(teamScore < .8 ? null : "Database", result.RecommendedTeam);
        Assert.Equal(review, result.NeedsHumanReview);
        Assert.Equal(new[] { "category", "priority:Database", "team" }, predictors.Calls);

        await using var db = fixture.CreateContext();
        var rows = await db.TicketPredictions.Where(p => p.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Single(rows.Select(row => row.AnalysisId).Distinct());
        Assert.All(rows, row => { Assert.NotNull(row.AnalysisId); Assert.Equal(1, row.InputRevision); Assert.Equal(.8, row.ReviewThreshold); });
        var routing = rows.Single(row => row.Type == PredictionType.TeamRouting);
        Assert.Equal(result.RecommendedTeam, routing.RecommendedTeamName);
        Assert.Equal(teamScore < .8, routing.NeedsHumanReview);
        Assert.Null(routing.RecommendedTeamId);
        var current = await db.Tickets.SingleAsync(t => t.Id == ticket.Id);
        Assert.Null(current.Category); Assert.Null(current.Priority); Assert.Null(current.AssignedTeamId);
        Assert.Equal(ticket.UpdatedAt, current.UpdatedAt);
        Assert.Equal(1, current.Revision); Assert.Equal(TicketStatus.Open, current.Status);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.EntityId == ticket.Id));

        var details = await client.GetFromJsonAsync<JsonElement>($"/api/tickets/{ticket.Id}");
        Assert.Equal(3, details.GetProperty("predictions").GetArrayLength());
        Assert.All(details.GetProperty("predictions").EnumerateArray(), row => Assert.Equal(rows[0].AnalysisId, row.GetProperty("analysisId").GetGuid()));
    }

    [Fact]
    public async Task ServerReviewThresholdsAreAppliedAndPersisted()
    {
        var ticket = await Seed();
        await using var factory = Factory(new Predictors(), .95, .96);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IntelligenceResult>(Json);
        Assert.NotNull(result);
        Assert.True(result.NeedsHumanReview);
        Assert.Equal("Database", result.RecommendedTeam);
        await using var db = fixture.CreateContext();
        var rows = await db.TicketPredictions.Where(p => p.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(.95, rows.Single(p => p.Type == PredictionType.Category).ReviewThreshold);
        Assert.Equal(.96, rows.Single(p => p.Type == PredictionType.Priority).ReviewThreshold);
        Assert.Equal(.8, rows.Single(p => p.Type == PredictionType.TeamRouting).ReviewThreshold);
    }

    [Fact]
    public async Task RealModelsAreComposedAndRepeatAnalysisRetainsBothRuns()
    {
        var ticket = await Seed();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        var category = await factory.Services.GetRequiredService<ITicketCategoryPredictor>()
            .PredictAsync(new(ticket.Title, ticket.Description));
        var priority = await factory.Services.GetRequiredService<ITicketPriorityPredictor>()
            .PredictAsync(new(ticket.Title, ticket.Description, category.Category));
        var team = await factory.Services.GetRequiredService<ITicketTeamPredictor>()
            .PredictAsync(new(ticket.Title, ticket.Description));
        for (var run = 0; run < 2; run++)
        {
            using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<IntelligenceResult>(Json);
            Assert.Equal(new IntelligenceResult(category.Category, category.Confidence, priority.PredictedPriority,
                priority.Confidence, team.Team, team.Confidence,
                category.Confidence < .8 || priority.Confidence < .8 || team.NeedsHumanReview), result);
        }
        await using var db = fixture.CreateContext();
        var rows = await db.TicketPredictions.Where(row => row.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(6, rows.Count);
        Assert.All(rows.GroupBy(row => row.AnalysisId), group => Assert.Equal(3, group.Count()));
        Assert.Equal(2, rows.Select(row => row.AnalysisId).Distinct().Count());
    }

    [Theory]
    [InlineData(TicketStatus.Open, true)]
    [InlineData(TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Closed, false)]
    public async Task InactiveTicketsReturnConflictBeforeCallingAnyModel(TicketStatus status, bool archived)
    {
        var ticket = await Seed(status, archived);
        var predictors = new Predictors();
        await using var factory = Factory(predictors);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        await AssertProblem(response, HttpStatusCode.Conflict);
        Assert.Empty(predictors.Calls);
        await AssertNoPredictions(ticket.Id);
    }

    [Fact]
    public async Task InProgressTicketsCanBeAnalyzed()
    {
        var ticket = await Seed(TicketStatus.InProgress);
        await using var factory = Factory(new Predictors());
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownAndEmptyTicketIdsUseProblemDetails()
    {
        var predictors = new Predictors();
        await using var factory = Factory(predictors);
        using var client = factory.CreateClient();
        using var missing = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/analyze", null);
        await AssertProblem(missing, HttpStatusCode.NotFound);
        using var empty = await client.PostAsync($"/api/tickets/{Guid.Empty}/analyze", null);
        await AssertProblem(empty, HttpStatusCode.BadRequest);
        Assert.Empty(predictors.Calls);
    }

    [Fact]
    public async Task ModelFailureReturnsProblemDetailsAndDoesNotSaveAnyPrediction()
    {
        var ticket = await Seed();
        await using var factory = Factory(new Predictors { FailTeam = true });
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        await AssertProblem(response, HttpStatusCode.InternalServerError);
        await AssertNoPredictions(ticket.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditOrArchiveDuringInferenceRejectsStaleAnalysisWithoutPartialHistory(bool archive)
    {
        var ticket = await Seed();
        var predictors = new Predictors
        {
            DuringPriority = async () =>
            {
                await using var db = fixture.CreateContext();
                var edited = await db.Tickets.SingleAsync(t => t.Id == ticket.Id);
                if (archive) edited.Archive(DateTimeOffset.UtcNow);
                else edited.UpdateDetails("Corrected ticket title", "Changed while models were running.", DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
        };
        await using var factory = Factory(predictors);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        await AssertProblem(response, HttpStatusCode.Conflict);
        await AssertNoPredictions(ticket.Id);
    }

    [Fact]
    public async Task ADatabaseFailureRollsBackTheEntireBatch()
    {
        var ticket = await Seed();
        var existingId = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            db.TicketPredictions.Add(TicketPrediction.ForPriority(existingId, ticket.Id, TicketPriority.Low,
                new(.9), "existing", 1, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var run = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        TicketPrediction[] batch =
        [
            TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database, new(.9), "test", 1, now, run, .8),
            TicketPrediction.ForPriority(existingId, ticket.Id, TicketPriority.High, new(.9), "test", 1, now, run, .8),
            TicketPrediction.ForTeamRecommendation(Guid.NewGuid(), ticket.Id, "Database", new(.9), "test", 1, now, run, .8)
        ];
        await using var factory = Factory(new Predictors());
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITicketIntelligenceStore>();
        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(ticket.Id, 1, batch));
        await using var verify = fixture.CreateContext();
        var rows = await verify.TicketPredictions.Where(p => p.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(existingId, Assert.Single(rows).Id);
        Assert.False(await verify.TicketPredictions.AnyAsync(p => p.AnalysisId == run));
    }

    [Fact]
    public async Task DatabaseRejectsAnInconsistentReviewDecision()
    {
        var ticket = await Seed();
        await using var factory = Factory(new Predictors());
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{ticket.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.ticket_predictions (id, ticket_id, type, confidence, model_version, input_revision, created_at, updated_at, predicted_category, needs_human_review, review_threshold) VALUES ({Guid.NewGuid()}, {ticket.Id}, 'Category', 0.9, 'invalid-review', 1, {now}, {now}, 'Database', true, 0.8)"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_predictions_review", error.ConstraintName);
    }

    [Fact]
    public async Task OpenApiDocumentsTheBodylessAnalyzeEndpointAndResult()
    {
        await using var factory = Factory(new Predictors());
        using var client = factory.CreateClient();
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var operation = spec.GetProperty("paths").GetProperty("/api/tickets/{id}/analyze").GetProperty("post");
        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        var fields = spec.GetProperty("components").GetProperty("schemas").GetProperty("IntelligenceResult").GetProperty("properties");
        Assert.Equal(7, fields.EnumerateObject().Count());
        Assert.Contains("null", fields.GetProperty("recommendedTeam").GetProperty("type").EnumerateArray().Select(t => t.GetString()));
    }

    private WebApplicationFactory<Api.Program> Factory(Predictors? predictors = null,
        double categoryThreshold = .8, double priorityThreshold = .8) =>
        new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = fixture.ConnectionString,
              ["MachineLearning:CategoryMinimumConfidence"] = categoryThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
              ["MachineLearning:PriorityMinimumConfidence"] = priorityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
              ["MachineLearning:TeamMinimumConfidence"] = "0.8" }));
            if (predictors is not null) builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketCategoryPredictor>(); services.AddSingleton<ITicketCategoryPredictor>(predictors);
                services.RemoveAll<ITicketPriorityPredictor>(); services.AddSingleton<ITicketPriorityPredictor>(predictors);
                services.RemoveAll<ITicketTeamPredictor>(); services.AddSingleton<ITicketTeamPredictor>(predictors);
            });
        });

    private async Task<Ticket> Seed(TicketStatus status = TicketStatus.Open, bool archived = false)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        now = now.AddTicks(-(now.Ticks % 10)); // PostgreSQL timestamps have microsecond precision.
        var user = new User(Guid.NewGuid(), "Analysis test", EmailAddress.Create(Guid.NewGuid().ToString("N") + "@example.com"), UserRole.User, now);
        var id = Guid.NewGuid();
        var ticket = new Ticket(id, "AN-" + id.ToString("N")[..20], "SQL transactions blocked by locks",
            "Writes wait behind a long transaction. Lock waits are growing and orders cannot be processed.", user.Id, now);
        if (status != TicketStatus.Open) ticket.StartWork(now);
        if (status is TicketStatus.Resolved or TicketStatus.Closed) ticket.Resolve("Fixed", now);
        if (status == TicketStatus.Closed) ticket.Close(now);
        if (archived) ticket.Archive(now);
        await using var db = fixture.CreateContext();
        db.Users.Add(user); db.Tickets.Add(ticket); await db.SaveChangesAsync();
        return ticket;
    }

    private async Task AssertNoPredictions(Guid id)
    { await using var db = fixture.CreateContext(); Assert.False(await db.TicketPredictions.AnyAsync(p => p.TicketId == id)); }
    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    private sealed class Predictors : ITicketCategoryPredictor, ITicketPriorityPredictor, ITicketTeamPredictor
    {
        public List<string> Calls { get; } = [];
        public double CategoryScore { get; init; } = .94;
        public double TeamScore { get; init; } = .88;
        public bool FailTeam { get; init; }
        public Func<Task>? DuringPriority { get; init; }
        public Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default)
        { Calls.Add("category"); return Task.FromResult(new TicketCategoryPrediction(TicketCategory.Database, CategoryScore, "category-test")); }
        public async Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default)
        { Calls.Add("priority:" + input.Category); if (DuringPriority is not null) await DuringPriority(); return new(TicketPriority.High, .91, "priority-test"); }
        public Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default)
        {
            Calls.Add("team"); if (FailTeam) throw new InvalidOperationException("Model failure for test");
            return Task.FromResult(new TeamRecommendationPolicy().Apply("Database", TeamScore, "team-test"));
        }
    }
}
