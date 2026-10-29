using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.Knowledge;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Knowledge;

[Collection("Postgres")]
public sealed class KnowledgeApiTests(PostgresFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("Document")]
    [InlineData("Article")]
    [InlineData("TroubleshootingGuide")]
    [InlineData("Faq")]
    [InlineData("Procedure")]
    public async Task CompleteLifecyclePreparesAndReplacesChunksAtomically(string kind)
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var source = "kb/" + Guid.NewGuid();
        using var created = await client.PostAsJsonAsync("/api/knowledge", new { title = "SQL connection guide", content = "# SQL diagnosis\n\n" + string.Concat(Enumerable.Repeat("Check the SQL service and collect connection logs.\n", 100)), source, category = "Database", kind, format = "Markdown" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var document = (await created.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(Json))!;
        Assert.EndsWith("/api/knowledge/" + document.Id, created.Headers.Location?.ToString());
        Assert.Equal(kind, document.Kind.ToString()); Assert.Equal(1, document.Version);
        Assert.StartsWith("SQL diagnosis", document.CleanedContent); Assert.Equal("text-v1", document.ProcessingVersion);
        Assert.NotNull(document.ProcessedAt); Assert.True(document.Chunks.Count > 1);
        var oldChunkIds = document.Chunks.Select(c => c.Id).ToArray();
        foreach (var chunk in document.Chunks)
        {
            Assert.Equal(document.Id, chunk.Metadata.DocumentId); Assert.Equal(1, chunk.Metadata.DocumentVersion);
            Assert.Equal(source, chunk.Metadata.Source); Assert.Equal(kind, chunk.Metadata.Kind.ToString());
            Assert.Equal(chunk.Content, document.CleanedContent![chunk.Metadata.StartOffset..chunk.Metadata.EndOffset]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Content))), chunk.Metadata.ContentHash);
        }
        using var updated = await client.PutAsJsonAsync($"/api/knowledge/{document.Id}", Body(source, "Updated guide", "Restart the service after checking logs.", 1));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        document = (await updated.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(Json))!;
        Assert.Equal(2, document.Version); Assert.Single(document.Chunks);
        Assert.Equal(2, document.Chunks[0].Metadata.DocumentVersion);
        Assert.Equal("Updated guide", document.Chunks[0].Metadata.Title);
        await using (var db = fixture.CreateContext()) Assert.False(await db.KnowledgeChunks.AnyAsync(c => oldChunkIds.Contains(c.Id)));
        var read = (await client.GetFromJsonAsync<KnowledgeDocumentDto>($"/api/knowledge/{document.Id}", Json))!;
        Assert.Equal(document.CleanedContent, read.CleanedContent);
        Assert.Equal(document.Chunks[0], read.Chunks[0]);
        using var deleted = await client.DeleteAsync($"/api/knowledge/{document.Id}?version=2");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync($"/api/knowledge/{document.Id}");
        await Problem(missing, HttpStatusCode.NotFound);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.KnowledgeDocuments.AnyAsync(d => d.Id == document.Id));
        Assert.False(await verify.KnowledgeChunks.AnyAsync(c => c.DocumentId == document.Id));
        Assert.NotNull(factory.Services.GetService<IEmbeddingService>());
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IVectorStore>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\" \",\"content\":\"text\",\"source\":\"kb/test\"}")]
    [InlineData("{\"title\":\"Title\",\"content\":null,\"source\":\"kb/test\"}")]
    [InlineData("{\"title\":\"Title\",\"content\":\"bad\\u0000text\",\"source\":\"kb/test\"}")]
    [InlineData("{\"title\":\"Title\",\"content\":\"###\",\"source\":\"kb/test\",\"format\":\"Markdown\"}")]
    [InlineData("{\"title\":\"Title\",\"content\":\"Text\",\"source\":\"kb/test\",\"format\":\"Pdf\"}")]
    [InlineData("{\"title\":\"Title\",\"content\":\"Text\",\"source\":\"kb/test\",\"kind\":999}")]
    [InlineData("{\"title\":\"Title\",\"content\":\"Text\",\"source\":\"kb/test\",\"chunks\":[]}")]
    [InlineData("{malformed")]
    public async Task InvalidBodiesReturnValidationProblemDetails(string body)
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/knowledge", new StringContent(body, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MaximumLengthAndUnicodeContentAreSupportedButOversizedTextIsRejected()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        using var valid = await client.PostAsJsonAsync("/api/knowledge", new { title = "Unicode", content = string.Concat(Enumerable.Repeat("é🔧 ", 20000)), source = "kb/" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/api/knowledge", new { title = "Too long", content = new string('x', 100001), source = "kb/" + Guid.NewGuid() });
        await Problem(invalid, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListSupportsPaginationKindCategoryAndLiteralSearch()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N");
        var first = await Create(client, marker + " VPN 100%_failure", kind: "Faq");
        await Create(client, marker + " VPN ordinary failure");
        var page = (await client.GetFromJsonAsync<KnowledgePage>($"/api/knowledge?search={marker}&pageSize=1", Json))!;
        Assert.Equal(2, page.TotalCount); Assert.Single(page.Items);
        var page2 = (await client.GetFromJsonAsync<KnowledgePage>($"/api/knowledge?search={marker}&pageSize=1&page=2", Json))!;
        Assert.NotEqual(page.Items[0].Id, page2.Items[0].Id);
        var filtered = (await client.GetFromJsonAsync<KnowledgePage>($"/api/knowledge?search={marker}&category=Network&kind=Faq", Json))!;
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        var literal = (await client.GetFromJsonAsync<KnowledgePage>("/api/knowledge?search=%25_", Json))!;
        Assert.Contains(literal.Items, d => d.Id == first.Id);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("kind=Unknown")]
    [InlineData("category=999")]
    public async Task InvalidFiltersAreRejected(string query)
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/knowledge?" + query);
        await Problem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SourceUniquenessAndStaleVersionsPreserveExistingChunks()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var first = await Create(client); var second = await Create(client);
        using var duplicate = await client.PostAsJsonAsync("/api/knowledge", new { title = "Duplicate", content = "Text", source = first.Source });
        await Problem(duplicate, HttpStatusCode.Conflict);
        using var duplicateUpdate = await client.PutAsJsonAsync($"/api/knowledge/{second.Id}", Body(first.Source, "Conflict", "New text", 1));
        await Problem(duplicateUpdate, HttpStatusCode.Conflict);
        using var stale = await client.PutAsJsonAsync($"/api/knowledge/{first.Id}", Body(first.Source, "Stale", "New text", 2));
        await Problem(stale, HttpStatusCode.Conflict);
        using var staleDelete = await client.DeleteAsync($"/api/knowledge/{first.Id}?version=2");
        await Problem(staleDelete, HttpStatusCode.Conflict);
        var unchanged = (await client.GetFromJsonAsync<KnowledgeDocumentDto>($"/api/knowledge/{second.Id}", Json))!;
        Assert.Equal(second.Title, unchanged.Title); Assert.Equal(1, unchanged.Version);
        Assert.Equal(second.Chunks[0], unchanged.Chunks[0]);
    }

    [Fact]
    public async Task MissingDocumentsEmptyIdsAndUnversionedDeletesAreHandled()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var id = Guid.NewGuid();
        using var get = await client.GetAsync($"/api/knowledge/{id}"); await Problem(get, HttpStatusCode.NotFound);
        using var update = await client.PutAsJsonAsync($"/api/knowledge/{id}", Body("kb/no", "Title", "Text", 1)); await Problem(update, HttpStatusCode.NotFound);
        using var delete = await client.DeleteAsync($"/api/knowledge/{id}?version=1"); await Problem(delete, HttpStatusCode.NotFound);
        using var unversioned = await client.DeleteAsync($"/api/knowledge/{id}"); await Problem(unversioned, HttpStatusCode.BadRequest);
        using var empty = await client.GetAsync($"/api/knowledge/{Guid.Empty}"); await Problem(empty, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProcessingFailureLeavesDocumentAndChunksUntouched()
    {
        KnowledgeDocumentDto document;
        await using (var factory = Factory()) { using var client = factory.CreateClient(); document = await Create(client); }
        await using var failing = Factory(services => services.AddScoped<IKnowledgeDocumentProcessor, FailedProcessor>());
        using var caller = failing.CreateClient();
        using var response = await caller.PutAsJsonAsync($"/api/knowledge/{document.Id}", Body(document.Source, "Changed", "Text", 1));
        await Problem(response, HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateContext();
        var current = await db.KnowledgeDocuments.Include(d => d.Chunks).SingleAsync(d => d.Id == document.Id);
        Assert.Equal(1, current.Version); Assert.Equal(document.Title, current.Title);
        Assert.Equal(document.Chunks[0].Id, Assert.Single(current.Chunks).Id);
    }

    [Fact]
    public async Task EfOptimisticConcurrencyRejectsASecondWriter()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var document = await Create(client);
        await using var first = fixture.CreateContext(); await using var second = fixture.CreateContext();
        var a = await first.KnowledgeDocuments.Include(d => d.Chunks).SingleAsync(d => d.Id == document.Id);
        var b = await second.KnowledgeDocuments.Include(d => d.Chunks).SingleAsync(d => d.Id == document.Id);
        a.Update("First", "Updated", a.Source, a.Category, DateTimeOffset.UtcNow);
        b.Update("Second", "Updated", b.Source, b.Category, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task MarkdownExtractionPreservesCodeAndNeverFetchesUrls()
    {
        const string markdown = "# Guide\r\n\r\nCheck **SQL** and [the procedure](https://127.0.0.1:1/no-fetch).\n```sql\n    SELECT '**value**';\n```";
        var processor = new KnowledgeDocumentProcessor(new TextKnowledgeExtractor());
        var result = await processor.PrepareAsync(new(Guid.NewGuid(), 1, "Guide", markdown,
            "https://127.0.0.1:1/no-fetch", TicketCategory.Database, KnowledgeDocumentKind.Procedure, KnowledgeContentFormat.Markdown));
        Assert.Equal("Guide\n\nCheck SQL and the procedure.\n    SELECT '**value**';", result.CleanedContent);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextKnowledgeExtractor().ExtractAsync(markdown, KnowledgeContentFormat.Markdown, cts.Token));
    }

    [Fact]
    public async Task OpenApiContainsAllFiveOperations()
    {
        await using var factory = Factory(); using var client = factory.CreateClient();
        var spec = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = spec.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/knowledge").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/knowledge").TryGetProperty("get", out _));
        foreach (var verb in new[] { "get", "put", "delete" }) Assert.True(paths.GetProperty("/api/knowledge/{id}").TryGetProperty(verb, out _));
    }

    [Fact]
    public async Task AnUnloadedChunkAggregateCannotBeUpdatedAndChunksCannotBeRewritten()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var document = await Create(client);
        await using (var db = fixture.CreateContext())
        {
            var partial = await db.KnowledgeDocuments.SingleAsync(d => d.Id == document.Id);
            partial.Update("Changed", "Text", partial.Source, partial.Category, DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.CreateContext())
        {
            var chunk = await db.KnowledgeChunks.SingleAsync(c => c.DocumentId == document.Id);
            db.Entry(chunk).Property(c => c.Content).CurrentValue = "Rewritten";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task MarkdownWithManyUnclosedBracketsIsProcessedAsText()
    {
        var result = await new KnowledgeDocumentProcessor(new TextKnowledgeExtractor()).PrepareAsync(new(Guid.NewGuid(), 1,
            "Malformed Markdown", new string('[', 100000), "kb/malformed", TicketCategory.Other,
            KnowledgeDocumentKind.Document, KnowledgeContentFormat.Markdown));
        Assert.Equal(100000, result.CleanedContent.Length);
    }

    private WebApplicationFactory<Api.Program> Factory(Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
            if (configure is not null) builder.ConfigureServices(configure);
        });
    private static object Body(string source, string title, string content, int version) => new { title, content, source, category = "Network", kind = "Procedure", format = "PlainText", version };
    private static async Task<KnowledgeDocumentDto> Create(HttpClient client, string title = "VPN guide", string kind = "Article")
    {
        using var response = await client.PostAsJsonAsync("/api/knowledge", new { title, content = "Check VPN connectivity.", source = "kb/" + Guid.NewGuid(), category = "Network", kind });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(Json))!;
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal((int)status, body.GetProperty("status").GetInt32()); Assert.True(body.TryGetProperty("traceId", out _));
    }
    private sealed class FailedProcessor : IKnowledgeDocumentProcessor
    { public Task<PreparedKnowledgeDocument> PrepareAsync(KnowledgeProcessingInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Test processing failure"); }
}
