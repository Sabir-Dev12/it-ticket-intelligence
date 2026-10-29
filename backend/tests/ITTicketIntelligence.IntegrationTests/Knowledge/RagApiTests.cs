using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Knowledge;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Knowledge;

[Collection("Postgres")]
public sealed class RagApiTests(PostgresFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task IndexRetrieveReindexUpdateAndDeletePreserveOnlyCurrentContext()
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        var sql = await Create(client, "SQL Server connection timeout", "SQL Server connection timeout: verify TCP port 1433, service health and firewall rules.");
        var vpn = await Create(client, "VPN troubleshooting", "VPN tunnel connection and remote network connectivity.");
        await Index(client, sql.Id); await Index(client, vpn.Id);
        var context = await Retrieve(client);
        var hit = Assert.Single(context.Results);
        Assert.Equal(sql.Id, hit.DocumentId); Assert.Equal(sql.Title, hit.Title); Assert.Equal(sql.Chunks[0].Content, hit.Chunk);
        Assert.Equal(1, hit.RelevanceScore, 6); Assert.Equal(sql.Source, hit.Metadata.Source);
        Assert.Equal(sql.Chunks[0].Id, hit.ChunkId); Assert.Equal(1, hit.Metadata.DocumentVersion);
        await Index(client, sql.Id);
        Assert.Equal(1, await VectorCount(sql.Id));
        using var updated = await client.PutAsJsonAsync($"/api/knowledge/{sql.Id}", new
        { title = "SQL Server timeout procedure v2", content = "SQL Server timeout: review the login timeout and current firewall policy.", source = sql.Source, category = "Database", kind = "Procedure", format = "PlainText", version = 1 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(0, await VectorCount(sql.Id));
        Assert.Empty((await Retrieve(client)).Results);
        await Index(client, sql.Id);
        Assert.Equal(2, Assert.Single((await Retrieve(client)).Results).Metadata.DocumentVersion);
        using var deleted = await client.DeleteAsync($"/api/knowledge/{sql.Id}?version=2"); Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await VectorCount(sql.Id)); Assert.Empty((await Retrieve(client)).Results);
    }

    [Fact]
    public async Task ExactCosineRankingFilteringAndProfileIsolationUsePersistedVectors()
    {
        var service = new TestEmbeddings(); await using var factory = Factory(service); using var client = factory.CreateClient();
        var a = await Create(client, "SQL primary", "SQL text."); var b = await Create(client, "SQL secondary", "SQL related.");
        await using var scope = factory.Services.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        static KnowledgeVector[] Record(KnowledgeDocumentDto d, string space, float[] values) => [new(d.Chunks[0].Id, d.Chunks[0].Content, d.Chunks[0].Metadata, new(space, values))];
        await store.ReplaceDocumentAsync(a.Id, 1, service.Profile, Record(a, service.Profile.Id, [1,0,0]));
        await store.ReplaceDocumentAsync(a.Id, 1, service.Profile, Record(a, service.Profile.Id, [1,0,0]));
        await store.ReplaceDocumentAsync(b.Id, 1, service.Profile, Record(b, service.Profile.Id, [.8f,.6f,0]));
        var matches = await store.SearchAsync(new(service.Profile.Id, [1,0,0]), 5, .1);
        Assert.Equal(new[] { a.Id, b.Id }, matches.Select(m => m.Metadata.DocumentId));
        Assert.Equal(1, matches[0].Score, 6); Assert.Equal(.8, matches[1].Score, 6);
        Assert.Single(await store.SearchAsync(new(service.Profile.Id, [1,0,0]), 5, .9));
        Assert.Equal(b.Id, Assert.Single(await store.SearchAsync(new(service.Profile.Id, [1,0,0]), 5, .1, new(DocumentId: b.Id))).Metadata.DocumentId);
        Assert.Empty(await store.SearchAsync(new("other-profile", [1,0,0]), 5, -1));
        Assert.Empty(await store.SearchAsync(new(service.Profile.Id, [1,0,0]), 5, -1, new(Category: TicketCategory.Billing)));
        await store.DeleteDocumentAsync(a.Id);
        Assert.Equal(0, await VectorCount(a.Id));
        Assert.NotNull(await client.GetFromJsonAsync<KnowledgeDocumentDto>($"/api/knowledge/{a.Id}", Json));
    }

    [Fact]
    public async Task ProviderFailureReturns503AndPreservesPreviouslyIndexedVectors()
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        var document = await Create(client, "SQL timeout", "SQL timeout procedure."); await Index(client, document.Id);
        embeddings.Fail = true;
        using var index = await client.PostAsync($"/api/rag/documents/{document.Id}/index", null); await Problem(index, HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, await VectorCount(document.Id));
        using var search = await client.PostAsJsonAsync("/api/rag/retrieve", new { question = "SQL Server connection timeout" }); await Problem(search, HttpStatusCode.ServiceUnavailable);
        embeddings.Fail = false; Assert.Single((await Retrieve(client)).Results);
    }

    [Fact]
    public async Task ConcurrentDocumentChangeRejectsObsoleteIndexing()
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        var document = await Create(client, "SQL timeout", "SQL timeout procedure.");
        embeddings.DuringGeneration = async () =>
        {
            await using var db = fixture.CreateContext();
            var current = await db.KnowledgeDocuments.Include(d => d.Chunks).SingleAsync(d => d.Id == document.Id);
            current.Update("New title", "Updated text", current.Source, current.Category, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        };
        using var response = await client.PostAsync($"/api/rag/documents/{document.Id}/index", null);
        await Problem(response, HttpStatusCode.Conflict); Assert.Equal(0, await VectorCount(document.Id));
    }

    [Fact]
    public async Task UnpreparedMissingAndEmptyIdsAreRejectedWithoutEmbeddingCalls()
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        { db.KnowledgeDocuments.Add(new KnowledgeDocument(id, "Legacy SQL", "SQL text", "legacy/" + id, TicketCategory.Database, DateTimeOffset.UtcNow)); await db.SaveChangesAsync(); }
        using var unprepared = await client.PostAsync($"/api/rag/documents/{id}/index", null); await Problem(unprepared, HttpStatusCode.Conflict);
        using var missing = await client.PostAsync($"/api/rag/documents/{Guid.NewGuid()}/index", null); await Problem(missing, HttpStatusCode.NotFound);
        using var empty = await client.PostAsync($"/api/rag/documents/{Guid.Empty}/index", null); await Problem(empty, HttpStatusCode.BadRequest);
        Assert.Equal(0, embeddings.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"question\":\" \"}")]
    [InlineData("{\"question\":\"SQL\",\"limit\":0}")]
    [InlineData("{\"question\":\"SQL\",\"minimumScore\":1.1}")]
    [InlineData("{\"question\":\"SQL\",\"documentId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"question\":\"SQL\",\"generateAnswer\":true}")]
    public async Task InvalidRetrievalRequestsUseValidationProblemDetails(string body)
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/rag/retrieve", new StringContent(body, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest); Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task DatabaseRejectsNonfiniteVectors()
    {
        var embeddings = new TestEmbeddings(); await using var factory = Factory(embeddings); using var client = factory.CreateClient();
        var document = await Create(client, "SQL test", "SQL test."); var now = DateTimeOffset.UtcNow;
        await using var db = fixture.CreateContext(); var values = new[] { float.NaN, 1f, 0f };
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.rag_vectors (chunk_id, profile, dimensions, embedding, created_at, updated_at) VALUES ({document.Chunks[0].Id}, 'invalid', 3, {values}, {now}, {now})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task RagEndpointsAreDocumentedWithoutAnAnswerField()
    {
        await using var factory = Factory(new TestEmbeddings()); using var client = factory.CreateClient();
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = spec.GetProperty("paths"); Assert.True(paths.GetProperty("/api/rag/retrieve").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/rag/documents/{id}/index").TryGetProperty("post", out _));
        Assert.False(spec.GetProperty("components").GetProperty("schemas").GetProperty("RetrievedContext").GetProperty("properties").TryGetProperty("answer", out _));
    }

    private WebApplicationFactory<Api.Program> Factory(TestEmbeddings embeddings) => new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
        builder.ConfigureServices(services => { services.RemoveAll<IEmbeddingService>(); services.AddSingleton<IEmbeddingService>(embeddings); });
    });
    private static async Task<KnowledgeDocumentDto> Create(HttpClient client, string title, string content)
    {
        using var response = await client.PostAsJsonAsync("/api/knowledge", new { title, content, source = "rag-tests/" + Guid.NewGuid(), category = "Database", kind = "Procedure" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(Json))!;
    }
    private static async Task Index(HttpClient client, Guid id)
    { using var response = await client.PostAsync($"/api/rag/documents/{id}/index", null); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
    private static async Task<RetrievedContext> Retrieve(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/rag/retrieve", new { question = "SQL Server connection timeout", minimumScore = .5 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<RetrievedContext>(Json))!;
    }
    private async Task<int> VectorCount(Guid id)
    { await using var db = fixture.CreateContext(); return await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM app.rag_vectors v JOIN app.knowledge_chunks c ON c.id=v.chunk_id WHERE c.document_id={id}").SingleAsync(); }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    { Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType); var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal((int)status, body.GetProperty("status").GetInt32()); Assert.True(body.TryGetProperty("traceId", out _)); }

    private sealed class TestEmbeddings : IEmbeddingService
    {
        public EmbeddingProfile Profile { get; } = new("test-" + Guid.NewGuid().ToString("N"), 3);
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Func<Task>? DuringGeneration { get; set; }
        public async Task<IReadOnlyList<TextEmbedding>> GenerateAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
        {
            Calls++; cancellationToken.ThrowIfCancellationRequested(); if (Fail) throw new RagUnavailableException("Test provider unavailable");
            if (DuringGeneration is not null) await DuringGeneration();
            return texts.Select(t => new TextEmbedding(Profile.Id, t.Contains("SQL", StringComparison.OrdinalIgnoreCase) ? new float[] {1,0,0} : new float[] {0,1,0})).ToArray();
        }
    }
}
