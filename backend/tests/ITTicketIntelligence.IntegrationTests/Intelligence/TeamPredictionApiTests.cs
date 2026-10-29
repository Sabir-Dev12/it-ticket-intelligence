using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Application.Intelligence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

public sealed class TeamPredictionApiTests
{
    private static object Input => new { title = "SQL transaction lock waits", description = "Production writes wait behind a long database transaction and the order queue keeps growing." };

    [Fact]
    public async Task HttpResultMatchesTheLoadedPredictorAndIsReadOnly()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        var predictor = factory.Services.GetRequiredService<ITicketTeamPredictor>();
        var expected = await predictor.PredictAsync(new TicketTeamInput("SQL transaction lock waits", "Production writes wait behind a long database transaction and the order queue keeps growing."));
        using var response = await client.PostAsJsonAsync("/api/predictions/team", Input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected.Team, body.GetProperty("team").GetString());
        Assert.Equal(expected.Confidence, body.GetProperty("confidence").GetDouble(), 7);
        Assert.Equal(expected.NeedsHumanReview, body.GetProperty("needsHumanReview").GetBoolean());
        Assert.Equal(expected.Threshold, body.GetProperty("threshold").GetDouble());
        using var scope = factory.Services.CreateScope();
        Assert.Same(predictor, scope.ServiceProvider.GetRequiredService<ITicketTeamPredictor>());
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0.01", false)]
    public async Task ServerThresholdControlsAbstentionWithoutRetraining(string threshold, bool review)
    {
        await using var factory = Factory(new Dictionary<string, string?> { ["MachineLearning:TeamMinimumConfidence"] = threshold });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/team", Input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(review, body.GetProperty("needsHumanReview").GetBoolean());
        if (review) Assert.Equal(JsonValueKind.Null, body.GetProperty("team").ValueKind);
        else Assert.Contains(body.GetProperty("team").GetString(), TicketTeamNames.All);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\" \",\"description\":\"Description\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":null}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"category\":\"Database\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"priority\":\"High\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"threshold\":0}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"needsHumanReview\":false}")]
    [InlineData("{malformed")]
    public async Task InvalidInputAndAttemptsToOverrideTheReviewPolicyAreRejected(string json)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/predictions/team", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("invalid")]
    public void InvalidThresholdStopsStartup(string threshold)
    {
        using var factory = Factory(new Dictionary<string, string?> { ["MachineLearning:TeamMinimumConfidence"] = threshold });
        Assert.ThrowsAny<ArgumentException>(() => factory.CreateClient());
    }

    [Fact]
    public void MissingTeamModelStopsStartupWithoutCreatingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-team-" + Guid.NewGuid());
        using var factory = Factory(new Dictionary<string, string?> { ["MachineLearning:TeamModelDirectory"] = path });
        Assert.Throws<InvalidDataException>(() => factory.CreateClient());
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task TeamPredictionDoesNotCallOtherPredictorsOrTheDatabase()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineLearning:CategoryModelDirectory"] = "missing-category",
                ["MachineLearning:PriorityModelDirectory"] = "missing-priority",
                ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Timeout=1"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketCategoryPredictor>();
                services.RemoveAll<ITicketPriorityPredictor>();
                services.AddSingleton<ITicketCategoryPredictor, ForbiddenCategoryPredictor>();
                services.AddSingleton<ITicketPriorityPredictor, ForbiddenPriorityPredictor>();
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/team", Input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TeamPortCanBeReplacedWithoutLoadingATeamArtifact()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["MachineLearning:TeamModelDirectory"] = "missing-team" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketTeamPredictor>();
                services.AddSingleton<ITicketTeamPredictor, ReplacementTeamPredictor>();
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/team", Input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Database", body.GetProperty("team").GetString());
        Assert.False(body.GetProperty("needsHumanReview").GetBoolean());
        Assert.Equal("replacement-team", body.GetProperty("modelVersion").GetString());
    }

    [Fact]
    public async Task OpenApiContainsTheNullableTeamAndReviewFlag()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(document.GetProperty("paths").GetProperty("/api/predictions/team").TryGetProperty("post", out _));
        var properties = document.GetProperty("components").GetProperty("schemas").GetProperty("TeamRecommendation").GetProperty("properties");
        Assert.True(properties.TryGetProperty("team", out _));
        Assert.True(properties.TryGetProperty("needsHumanReview", out _));
        Assert.True(properties.TryGetProperty("threshold", out _));
    }

    private static WebApplicationFactory<Api.Program> Factory(Dictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            if (settings is not null) builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

    private sealed class ForbiddenCategoryPredictor : ITicketCategoryPredictor
    {
        public Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Team must not call Category.");
    }
    private sealed class ForbiddenPriorityPredictor : ITicketPriorityPredictor
    {
        public Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Team must not call Priority.");
    }
    private sealed class ReplacementTeamPredictor : ITicketTeamPredictor
    {
        public Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default) => Task.FromResult(new TeamRecommendationPolicy().Apply("Database", 0.91, "replacement-team"));
    }
}
