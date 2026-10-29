using System.Security.Cryptography;
using System.Text;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Knowledge;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Knowledge;

public sealed class KnowledgeModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static KnowledgeProcessingInput Input(string content) => new(Guid.NewGuid(), 2, "SQL guide", content,
        "kb/sql.md", TicketCategory.Database, KnowledgeDocumentKind.TroubleshootingGuide, KnowledgeContentFormat.PlainText);

    [Fact]
    public async Task CleansTextAndBuildsMetadataWithExactHashesAndOffsets()
    {
        var input = Input("\uFEFF\r\n# extracted\r\n\r\n\r\n    keep indentation\t\u0007  \r\nCafe\u0301\r\n");
        using var cts = new CancellationTokenSource();
        var extractor = new Extractor();
        var prepared = await new KnowledgeDocumentProcessor(extractor).PrepareAsync(input, cts.Token);
        Assert.Equal("# extracted\n\n    keep indentation\nCafé", prepared.CleanedContent);
        Assert.Equal(cts.Token, extractor.Token);
        Assert.Equal("text-v1", prepared.ProcessingVersion);
        var chunk = Assert.Single(prepared.Chunks);
        Assert.Equal(prepared.CleanedContent, chunk.Content);
        Assert.Equal(input.DocumentId, chunk.Metadata.DocumentId);
        Assert.Equal(2, chunk.Metadata.DocumentVersion);
        Assert.Equal(input.Title, chunk.Metadata.Title);
        Assert.Equal(input.Source, chunk.Metadata.Source);
        Assert.Equal(input.Category, chunk.Metadata.Category);
        Assert.Equal(input.Kind, chunk.Metadata.Kind);
        Assert.Equal(0, chunk.Metadata.StartOffset);
        Assert.Equal(chunk.Content.Length, chunk.Metadata.EndOffset);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Content))), chunk.Metadata.ContentHash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(100000)]
    public async Task ChunkingIsBoundedOverlappingAndCoversAllText(int length)
    {
        var prepared = await new KnowledgeDocumentProcessor(new Extractor()).PrepareAsync(Input(new string('x', length)));
        Assert.Equal(0, prepared.Chunks[0].Metadata.StartOffset);
        Assert.Equal(length, prepared.Chunks[^1].Metadata.EndOffset);
        for (var i = 0; i < prepared.Chunks.Count; i++)
        {
            var chunk = prepared.Chunks[i];
            Assert.Equal(i, chunk.Metadata.Position);
            Assert.InRange(chunk.Content.Length, 1, 1200);
            Assert.Equal(chunk.Content, prepared.CleanedContent[chunk.Metadata.StartOffset..chunk.Metadata.EndOffset]);
            if (i > 0) Assert.Equal(150, prepared.Chunks[i - 1].Metadata.EndOffset - chunk.Metadata.StartOffset);
        }
    }

    [Fact]
    public async Task ChunkBoundariesDoNotSplitSurrogatePairsAndOutputIsDeterministic()
    {
        var input = Input(new string('x', 1199) + "🔧" + string.Concat(Enumerable.Repeat("é🔧 ", 700)));
        var processor = new KnowledgeDocumentProcessor(new Extractor());
        var first = await processor.PrepareAsync(input);
        var second = await processor.PrepareAsync(input);
        Assert.Equal(first.Chunks.ToArray(), second.Chunks.ToArray());
        Assert.All(first.Chunks, chunk =>
        {
            Assert.False(char.IsLowSurrogate(chunk.Content[0]));
            Assert.False(char.IsHighSurrogate(chunk.Content[^1]));
        });
    }

    [Fact]
    public async Task ProcessingCancellationAndEmptyExtractedContentAreRejected()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var extractor = new Extractor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeDocumentProcessor(extractor).PrepareAsync(Input("Text"), cts.Token));
        Assert.Equal(0, extractor.Calls);
        await Assert.ThrowsAsync<RequestValidationException>(() => new KnowledgeDocumentProcessor(new Extractor { Output = "\u0000\r\n\uFEFF" }).PrepareAsync(Input("Text")));
        await Assert.ThrowsAsync<RequestValidationException>(() => new KnowledgeDocumentProcessor(extractor).PrepareAsync(Input(new string('x', 100001))));
    }

    [Fact]
    public async Task CancellationAfterExtractionPreventsChunking()
    {
        using var cts = new CancellationTokenSource();
        var extractor = new Extractor { AfterExtract = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeDocumentProcessor(extractor).PrepareAsync(Input("Text"), cts.Token));
    }

    [Fact]
    public void DomainRejectsInvalidChunkCoordinatesAndStaleMetadata()
    {
        var document = Document();
        var hash = new string('a', 64);
        Assert.Throws<DomainException>(() => new KnowledgeChunk(Guid.NewGuid(), document.Id, 1, 0, "text", 0, 5, hash, Now));
        Assert.Throws<DomainException>(() => new KnowledgeChunk(Guid.NewGuid(), document.Id, 0, 0, "text", 0, 4, hash, Now));
        Assert.Throws<DomainException>(() => new KnowledgeChunk(Guid.NewGuid(), document.Id, 1, 0, "text", 0, 4, "invalid", Now));
        var stale = new KnowledgeChunk(Guid.NewGuid(), document.Id, 2, 0, "text", 0, 4, hash, Now);
        Assert.Throws<DomainException>(() => document.ApplyPreparation("text", "test", [stale], Now));
        Assert.Empty(document.Chunks);
        Assert.Null(document.CleanedContent);
        var gap = new KnowledgeChunk(Guid.NewGuid(), document.Id, 1, 0, "ext", 1, 4, hash, Now);
        Assert.Throws<DomainException>(() => document.ApplyPreparation("text", "test", [gap], Now));
    }

    [Fact]
    public async Task DomainPreparationIsImmutableAndUpdateInvalidatesItsPreviousChunks()
    {
        var document = Document();
        var prepared = await new KnowledgeDocumentProcessor(new Extractor()).PrepareAsync(new(document.Id, 1,
            document.Title, document.Content, document.Source, document.Category, document.Kind, document.Format));
        var chunk = Assert.Single(prepared.Chunks);
        document.ApplyPreparation(prepared.CleanedContent, "test", [new KnowledgeChunk(Guid.NewGuid(), document.Id, 1, 0,
            chunk.Content, 0, chunk.Content.Length, chunk.Metadata.ContentHash, Now)], Now);
        Assert.True(((ICollection<KnowledgeChunk>)document.Chunks).IsReadOnly);
        document.Update("Revised", "New text", "kb/new", TicketCategory.Network, Now.AddSeconds(1));
        Assert.Equal(2, document.Version); Assert.Empty(document.Chunks);
        Assert.Null(document.CleanedContent); Assert.Null(document.ProcessingVersion); Assert.Null(document.ProcessedAt);
    }

    [Fact]
    public async Task CommandsPrepareBeforeSavingAndPropagateCancellation()
    {
        var fake = new Repository(); var extractor = new Extractor();
        var handler = Handler(fake, new KnowledgeDocumentProcessor(extractor));
        using var cts = new CancellationTokenSource();
        var created = await handler.HandleAsync(Create(), cts.Token);
        Assert.Equal(1, fake.Saves); Assert.Single(created.Chunks); Assert.Equal(cts.Token, fake.Token);
        var updated = await handler.HandleAsync(new UpdateKnowledgeCommand(created.Id, "Changed", "New content", "kb/new",
            TicketCategory.Network, KnowledgeDocumentKind.Faq, KnowledgeContentFormat.PlainText, 1), cts.Token);
        Assert.Equal(2, updated.Version); Assert.Equal(2, fake.Saves);
        Assert.Equal(KnowledgeDocumentKind.Faq, updated.Chunks[0].Metadata.Kind);
        Assert.True(await handler.HandleAsync(new DeleteKnowledgeCommand(created.Id, 2), cts.Token));
        Assert.Null(fake.Document); Assert.Equal(3, fake.Saves);
    }

    [Fact]
    public async Task ProcessingFailureDoesNotMutateTheLoadedDocumentOrSave()
    {
        var fake = new Repository { Document = Document() };
        var handler = Handler(fake, new FailingProcessor());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new UpdateKnowledgeCommand(fake.Document.Id,
            "New title", "New content", "kb/new", TicketCategory.Other, KnowledgeDocumentKind.Article, KnowledgeContentFormat.Markdown, 1)));
        Assert.Equal("Original", fake.Document.Title); Assert.Equal(1, fake.Document.Version); Assert.Equal(0, fake.Saves);
    }

    [Fact]
    public async Task MissingStaleOrInvalidCommandsDoNotWrite()
    {
        var fake = new Repository(); var handler = Handler(fake, new KnowledgeDocumentProcessor(new Extractor()));
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(Create() with { Title = " " }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.HandleAsync(new DeleteKnowledgeCommand(Guid.NewGuid(), 1)));
        fake.Document = Document();
        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new DeleteKnowledgeCommand(fake.Document.Id, 2)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(Create(), cts.Token));
        Assert.Equal(0, fake.Saves);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task UnknownOrInactiveAuthorsAreRejected(bool? active)
    {
        var fake = new Repository { AuthorActive = active };
        var handler = Handler(fake, new KnowledgeDocumentProcessor(new Extractor()));
        if (active is null) await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.HandleAsync(Create() with { CreatedById = Guid.NewGuid() }));
        else await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Create() with { CreatedById = Guid.NewGuid() }));
        Assert.Equal(0, fake.Saves);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(1000001, 20)]
    public async Task QueriesRejectInvalidPagination(int page, int pageSize)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => new KnowledgeQueryHandler(new Repository()).HandleAsync(new GetKnowledgePageQuery(page, pageSize)));
    }

    private static KnowledgeDocument Document() => new(Guid.NewGuid(), "Original", "text", "kb/old", TicketCategory.Other, Now);
    private static CreateKnowledgeCommand Create() => new("SQL guide", "Check database status.", "kb/sql", TicketCategory.Database, KnowledgeDocumentKind.Article, KnowledgeContentFormat.PlainText);
    private static KnowledgeCommandHandler Handler(Repository fake, IKnowledgeDocumentProcessor processor) => new(fake, processor, fake, new FixedClock());
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Extractor : IKnowledgeTextExtractor
    {
        public string? Output { get; init; }
        public Action? AfterExtract { get; init; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<string> ExtractAsync(string content, KnowledgeContentFormat format, CancellationToken cancellationToken = default)
        { Calls++; Token = cancellationToken; AfterExtract?.Invoke(); return Task.FromResult(Output ?? content); }
    }
    private sealed class FailingProcessor : IKnowledgeDocumentProcessor
    {
        public Task<PreparedKnowledgeDocument> PrepareAsync(KnowledgeProcessingInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Test failure");
    }
    private sealed class Repository : IKnowledgeRepository, IUnitOfWork
    {
        public KnowledgeDocument? Document { get; set; }
        public bool? AuthorActive { get; init; }
        public int Saves { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Document?.Id == id ? Document : null);
        public Task<KnowledgePage> GetPageAsync(GetKnowledgePageQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new KnowledgePage([], 0, query.Page, query.PageSize));
        public Task<bool?> IsAuthorActiveAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(AuthorActive);
        public void Add(KnowledgeDocument document) => Document = document;
        public void Remove(KnowledgeDocument document) => Document = null;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; Token = cancellationToken; return Task.FromResult(1); }
    }
}
