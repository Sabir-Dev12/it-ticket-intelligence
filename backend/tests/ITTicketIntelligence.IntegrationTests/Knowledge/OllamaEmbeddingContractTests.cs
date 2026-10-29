using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Knowledge;

public sealed class OllamaEmbeddingContractTests
{
    private static OllamaEmbeddingOptions Options => new() { Model = "test-embedding", ModelDigest = new string('a', 64),
        Dimensions = 3, BaseUrl = "http://embedding.test/ollama/", DocumentPrefix = "document: ", QueryPrefix = "query: " };

    [Theory]
    [InlineData(EmbeddingPurpose.Document, "document: ")]
    [InlineData(EmbeddingPurpose.Query, "query: ")]
    public async Task AdapterValidatesDigestUsesPurposePrefixesAndDisablesTruncation(EmbeddingPurpose purpose, string prefix)
    {
        var handler = new Handler(); using var service = new OllamaEmbeddingService(new HttpClient(handler), Options, NullLogger<OllamaEmbeddingService>.Instance);
        var output = await service.GenerateAsync(["SQL timeout"], purpose);
        Assert.Equal(new[] { "/ollama/api/tags", "/ollama/api/embed" }, handler.Paths);
        var request = handler.Request!.Value; Assert.Equal("test-embedding", request.GetProperty("model").GetString());
        Assert.False(request.GetProperty("truncate").GetBoolean()); Assert.Equal(prefix + "SQL timeout", request.GetProperty("input")[0].GetString());
        var vector = Assert.Single(output); Assert.Equal(service.Profile.Id, vector.Model);
        Assert.Equal(.6f, vector.Values[0], 6); Assert.Equal(.8f, vector.Values[1], 6);
    }

    [Theory]
    [InlineData("http-error")]
    [InlineData("html")]
    [InlineData("bad-json")]
    [InlineData("missing-model")]
    [InlineData("wrong-model")]
    [InlineData("wrong-count")]
    [InlineData("wrong-dimension")]
    [InlineData("zero-vector")]
    public async Task ProviderErrorsAndInvalidContractsBecomeUnavailable(string failure)
    {
        using var service = new OllamaEmbeddingService(new HttpClient(new Handler { Failure = failure }), Options, NullLogger<OllamaEmbeddingService>.Instance);
        await Assert.ThrowsAsync<RagUnavailableException>(() => service.GenerateAsync(["SQL timeout"], EmbeddingPurpose.Query));
    }

    [Fact]
    public async Task TimeoutIs503CompatibleButCallerCancellationIsPreserved()
    {
        var options = new OllamaEmbeddingOptions { Model = "test-embedding", ModelDigest = new string('a',64), Dimensions = 3, TimeoutSeconds = 1 };
        using var service = new OllamaEmbeddingService(new HttpClient(new Handler { Failure = "hang" }), options, NullLogger<OllamaEmbeddingService>.Instance);
        await Assert.ThrowsAsync<RagUnavailableException>(() => service.GenerateAsync(["SQL"], EmbeddingPurpose.Query));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateAsync(["SQL"], EmbeddingPurpose.Query, cts.Token));
    }

    [Fact]
    public void ModelRevisionDimensionsAndPrefixesDefineIndependentSpaces()
    {
        var profile = Options.ValidateAndGetProfile();
        Assert.NotEqual(profile.Id, new OllamaEmbeddingOptions { Model = Options.Model, ModelDigest = new string('b',64), Dimensions = 3 }.ValidateAndGetProfile().Id);
        Assert.NotEqual(profile.Id, new OllamaEmbeddingOptions { Model = Options.Model, ModelDigest = Options.ModelDigest, Dimensions = 2 }.ValidateAndGetProfile().Id);
        Assert.NotEqual(profile.Id, new OllamaEmbeddingOptions { Model = Options.Model, ModelDigest = Options.ModelDigest, Dimensions = 3, DocumentPrefix = "changed" }.ValidateAndGetProfile().Id);
        Assert.Throws<InvalidOperationException>(() => new OllamaEmbeddingOptions { BaseUrl = "file:///tmp/model" }.ValidateAndGetProfile());
        Assert.Throws<InvalidOperationException>(() => new OllamaEmbeddingOptions { ModelDigest = "mutable-tag" }.ValidateAndGetProfile());
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string? Failure { get; init; }
        public List<string> Paths { get; } = [];
        public JsonElement? Request { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (Failure == "hang") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = Failure == "missing-model" ? Array.Empty<object>() : new object[] { new { name = "test-embedding", digest = new string('a',64) } } }) };
            Request = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)).RootElement.Clone();
            if (Failure is "http-error" or "html") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<html>unavailable</html>") };
            if (Failure == "bad-json") return new(HttpStatusCode.OK) { Content = new StringContent("{malformed") };
            var values = Failure == "wrong-dimension" ? new[] { 1f,0f } : Failure == "zero-vector" ? new[] { 0f,0f,0f } : new[] { 3f,4f,0f };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { model = Failure == "wrong-model" ? "other" : "test-embedding", embeddings = Failure == "wrong-count" ? Array.Empty<float[]>() : new[] { values } }) };
        }
    }
}
