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

public sealed class PriorityPredictionApiTests
{
    [Fact]
    public async Task HttpPredictionUsesTheLoadedPriorityModelAndSuppliedCategory()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        var input = new TicketPriorityInput("Production service is unavailable", "Every office is blocked and no alternate connection is available.", TicketCategory.Network);
        var predictor = factory.Services.GetRequiredService<ITicketPriorityPredictor>();
        var expected = await predictor.PredictAsync(input);
        using var response = await client.PostAsJsonAsync("/api/predictions/priority", new { input.Title, input.Description, category = input.Category.ToString() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected.PredictedPriority.ToString(), body.GetProperty("predictedPriority").GetString());
        Assert.Equal(expected.Confidence, body.GetProperty("confidence").GetDouble(), 7);
        Assert.Equal(expected.ModelVersion, body.GetProperty("modelVersion").GetString());
        using var scope = factory.Services.CreateScope();
        Assert.Same(predictor, scope.ServiceProvider.GetRequiredService<ITicketPriorityPredictor>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\"}")]
    [InlineData("{\"title\":\" \",\"description\":\"Description\",\"category\":\"Network\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":null,\"category\":\"Network\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"category\":\"Unknown\"}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"category\":1}")]
    [InlineData("{\"title\":\"Title\",\"description\":\"Description\",\"category\":\"Network\",\"priority\":\"Critical\"}")]
    [InlineData("{malformed")]
    public async Task InvalidInputAndTargetOverpostingReturnProblemDetails(string json)
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/predictions/priority", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task PriorityPortCanBeReplacedWithoutChangingTheControllerOrLoadingAnArtifact()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineLearning:PriorityModelDirectory"] = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid())
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketPriorityPredictor>();
                services.AddSingleton<ITicketPriorityPredictor, ReplacementPredictor>();
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/priority", new { title = "Title", description = "Description", category = "Network" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("High", body.GetProperty("predictedPriority").GetString());
        Assert.Equal(0.9, body.GetProperty("confidence").GetDouble());
        Assert.Equal("replacement-priority", body.GetProperty("modelVersion").GetString());
    }

    [Fact]
    public void MissingPriorityArtifactStopsStartupWithoutTraining()
    {
        using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineLearning:PriorityModelDirectory"] = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid())
            }));
        });
        var exception = Assert.Throws<InvalidDataException>(() => factory.CreateClient());
        Assert.Contains("Priority model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDescribesTheRequiredCategoryAndPriorityResult()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(document.GetProperty("paths").GetProperty("/api/predictions/priority").TryGetProperty("post", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Contains("category", schemas.GetProperty("PredictPriorityRequest").GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.True(schemas.GetProperty("TicketPriorityPrediction").GetProperty("properties").TryGetProperty("predictedPriority", out _));
        Assert.True(schemas.GetProperty("TicketPriorityPrediction").GetProperty("properties").TryGetProperty("confidence", out _));
    }

    private sealed class ReplacementPredictor : ITicketPriorityPredictor
    {
        public Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TicketPriorityPrediction(TicketPriority.High, 0.9, "replacement-priority"));
    }
}
