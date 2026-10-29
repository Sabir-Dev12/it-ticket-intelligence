using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

public sealed class CategoryPredictionApiTests
{
    [Fact]
    public async Task HttpPredictionUsesTheSavedModelAndReusesOnePredictor()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        var input = new TicketCategoryInput("Cannot download my invoice PDF", "The invoice appears online, but its PDF download is empty. Other account pages work normally.");
        var predictor = factory.Services.GetRequiredService<ITicketCategoryPredictor>();
        var expected = await predictor.PredictAsync(input);
        using var response = await client.PostAsJsonAsync("/api/predictions/category", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected.Category.ToString(), result.GetProperty("category").GetString());
        Assert.Equal(expected.Confidence, result.GetProperty("confidence").GetDouble(), 7);
        Assert.Equal(expected.ModelVersion, result.GetProperty("modelVersion").GetString());
        using var scope = factory.Services.CreateScope();
        Assert.Same(predictor, scope.ServiceProvider.GetRequiredService<ITicketCategoryPredictor>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\" \",\"description\":\"Description\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":null}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"category\":\"Billing\"}")]
    [InlineData("{malformed")]
    public async Task InvalidPredictionInputReturnsValidationProblemDetails(string json)
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/predictions/category", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task PredictorCanBeReplacedWithoutChangingTheHttpContractOrLoadingAnArtifact()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineLearning:CategoryModelDirectory"] = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid())
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketCategoryPredictor>();
                services.AddSingleton<ITicketCategoryPredictor, ReplacementPredictor>();
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/category", new { title = "Title", description = "Description" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Network", body.GetProperty("category").GetString());
        Assert.Equal(0.9, body.GetProperty("confidence").GetDouble());
        Assert.Equal("replacement-v2", body.GetProperty("modelVersion").GetString());
    }

    [Fact]
    public void ApiStartupFailsOnMissingArtifactWithoutAttemptingTraining()
    {
        using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineLearning:CategoryModelDirectory"] = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid())
            }));
        });
        Assert.Throws<InvalidDataException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task OpenApiExposesThePredictionInputAndResult()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(document.GetProperty("paths").GetProperty("/api/predictions/category").TryGetProperty("post", out _));
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("TicketCategoryPrediction");
        Assert.True(schema.GetProperty("properties").TryGetProperty("confidence", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("modelVersion", out _));
    }

    private sealed class ReplacementPredictor : ITicketCategoryPredictor
    {
        public Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TicketCategoryPrediction(TicketCategory.Network, 0.9, "replacement-v2"));
    }
}
