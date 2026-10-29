using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Knowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Knowledge;

public sealed class RagTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static KnowledgeChunkMetadata Metadata(Guid? id = null, int version = 1) => new(id ?? Guid.NewGuid(), version,
        "SQL Server guide", "kb/sql", TicketCategory.Database, KnowledgeDocumentKind.Procedure,
        KnowledgeContentFormat.PlainText, 0, 0, 4, new string('a', 64));

    [Fact]
    public async Task RetrieverEmbedsQuestionSortsResultsAndUsesCurrentCanonicalMetadata()
    {
        var fake = new Dependencies(); var metadata = Metadata();
        var high = new KnowledgeChunkDto(Guid.NewGuid(), "Check SQL Server connectivity.", metadata);
        var low = new KnowledgeChunkDto(Guid.NewGuid(), "Collect logs.", metadata with { Title = "Logs" });
        fake.Current = [low, high];
        fake.Matches = [new(low.Id, low.Metadata, .6), new(high.Id, high.Metadata with { Title = "Untrusted cached title" }, .9), new(high.Id, high.Metadata, .85)];
        using var cts = new CancellationTokenSource();
        var result = await fake.Retriever().RetrieveAsync(new("SQL Server connection timeout", 2), cts.Token);
        Assert.Equal("test-space", result.EmbeddingProfile);
        Assert.Equal(2, result.Results.Count);
        Assert.Equal(high.Id, result.Results[0].ChunkId);
        Assert.Equal(high.Metadata.Title, result.Results[0].Title);
        Assert.Equal(high.Content, result.Results[0].Chunk);
        Assert.Equal(.9, result.Results[0].RelevanceScore);
        Assert.Equal(EmbeddingPurpose.Query, Assert.Single(fake.Purposes));
        Assert.Equal("SQL Server connection timeout", Assert.Single(fake.Texts));
        Assert.All(fake.Tokens, token => Assert.Equal(cts.Token, token));
        Assert.Equal(8, fake.SearchLimit);
        Assert.DoesNotContain(fake.Log.Messages, message => message.Contains("connection timeout", StringComparison.Ordinal));
        Assert.Contains(fake.Log.Messages, message => message.Contains("retrieved 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletedStaleHashMismatchedAndFilteredCandidatesAreExcluded()
    {
        var fake = new Dependencies(); var metadata = Metadata(); var current = new KnowledgeChunkDto(Guid.NewGuid(), "text", metadata);
        fake.Current = [current];
        fake.Matches = [new(Guid.NewGuid(), metadata, .99), new(current.Id, metadata with { DocumentVersion = 0 }, .98),
            new(current.Id, metadata with { ContentHash = new string('b',64) }, .97)];
        Assert.Empty((await fake.Retriever().RetrieveAsync(new("SQL timeout"))).Results);
        fake.Matches = [new(current.Id, metadata, .9)];
        Assert.Empty((await fake.Retriever().RetrieveAsync(new("SQL timeout", Filter: new(Category: TicketCategory.Network)))).Results);
        Assert.Empty((await fake.Retriever().RetrieveAsync(new("SQL timeout", MinimumScore: .95))).Results);
    }

    [Fact]
    public async Task EmptyIndexReturnsAnEmptyContext()
    {
        var fake = new Dependencies();
        Assert.Empty((await fake.Retriever().RetrieveAsync(new("SQL timeout"))).Results);
    }

    [Theory]
    [InlineData("", 5, .35)]
    [InlineData(" ", 5, .35)]
    [InlineData("SQL", 0, .35)]
    [InlineData("SQL", 21, .35)]
    [InlineData("SQL", 5, double.NaN)]
    [InlineData("SQL", 5, 1.1)]
    public async Task InvalidQueriesDoNotCallDependencies(string question, int limit, double minimum)
    {
        var fake = new Dependencies();
        await Assert.ThrowsAsync<RequestValidationException>(() => fake.Retriever().RetrieveAsync(new(question, limit, minimum)));
        Assert.Empty(fake.Texts); Assert.Empty(fake.Tokens);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.1)]
    public async Task InvalidSimilarityScoresAreDependencyFailures(double score)
    {
        var fake = new Dependencies { Matches = [new(Guid.NewGuid(), Metadata(), score)] };
        await Assert.ThrowsAsync<RagUnavailableException>(() => fake.Retriever().RetrieveAsync(new("SQL timeout")));
    }

    [Fact]
    public async Task CancellationBeforeAndAfterEmbeddingPreventsRetrieval()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var fake = new Dependencies();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.Retriever().RetrieveAsync(new("SQL"), cts.Token));
        Assert.Empty(fake.Texts);
        using var during = new CancellationTokenSource(); fake.AfterEmbedding = during.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.Retriever().RetrieveAsync(new("SQL"), during.Token));
        Assert.Equal(0, fake.SearchCalls);
    }

    [Fact]
    public async Task IndexingBatchesChunksValidatesTheWholeBatchAndCommitsOnce()
    {
        var fake = new Dependencies { Document = Document(35) };
        using var cts = new CancellationTokenSource();
        var result = await fake.Indexer().IndexDocumentAsync(fake.Document.Id, cts.Token);
        Assert.Equal(new[] { 16, 16, 3 }, fake.BatchSizes);
        Assert.All(fake.Purposes, p => Assert.Equal(EmbeddingPurpose.Document, p));
        Assert.All(fake.Texts, t => Assert.StartsWith("SQL guide\n", t));
        Assert.Equal(1, fake.Writes); Assert.Equal(35, fake.Stored!.Count);
        Assert.Equal(35, result.ChunkCount); Assert.Equal(3, result.Dimensions); Assert.Equal(1, result.DocumentVersion);
        Assert.Equal(Now, result.IndexedAt);
        Assert.All(fake.Stored, v => { Assert.Equal(1, v.Metadata.DocumentVersion); Assert.Equal(fake.Profile.Id, v.Embedding.Model); });
    }

    [Fact]
    public async Task MissingUnpreparedAndEmptyIdsCannotBeIndexed()
    {
        var fake = new Dependencies();
        await Assert.ThrowsAsync<RequestValidationException>(() => fake.Indexer().IndexDocumentAsync(Guid.Empty));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => fake.Indexer().IndexDocumentAsync(Guid.NewGuid()));
        fake.Document = new(Guid.NewGuid(), "Title", "text", "kb/unprepared", TicketCategory.Other, Now);
        await Assert.ThrowsAsync<ConflictException>(() => fake.Indexer().IndexDocumentAsync(fake.Document.Id));
        Assert.Equal(0, fake.Writes);
    }

    [Fact]
    public async Task EmbeddingOrStorageFailureNeverReturnsSuccessfulIndexing()
    {
        var fake = new Dependencies { Document = Document(2), FailEmbedding = true };
        await Assert.ThrowsAsync<RagUnavailableException>(() => fake.Indexer().IndexDocumentAsync(fake.Document.Id));
        Assert.Equal(0, fake.Writes);
        fake.FailEmbedding = false; fake.FailStorage = true;
        await Assert.ThrowsAsync<ConflictException>(() => fake.Indexer().IndexDocumentAsync(fake.Document.Id));
    }

    [Theory]
    [InlineData("wrong-space", 3, 1)]
    [InlineData("test-space", 2, 1)]
    [InlineData("test-space", 3, 0)]
    [InlineData("test-space", 3, float.NaN)]
    public void IncompatibleZeroAndNonfiniteVectorsAreRejected(string model, int size, float value)
    {
        Assert.Throws<RagUnavailableException>(() => EmbeddingValidation.Normalize(new(model, Enumerable.Repeat(value, size).ToArray()), new("test-space", 3)));
    }

    [Fact]
    public void VectorsAreNormalizedUsingStableDoublePrecisionAccumulation()
    {
        var values = EmbeddingValidation.Normalize(new("test", [float.MaxValue, float.MaxValue]), new("test", 2));
        Assert.InRange(values.Sum(v => (double)v * v), .999999, 1.000001);
    }

    private static KnowledgeDocument Document(int count)
    {
        var content = string.Concat(Enumerable.Repeat("text", count));
        var document = new KnowledgeDocument(Guid.NewGuid(), "SQL guide", content, "kb/sql", TicketCategory.Database, Now);
        document.ApplyPreparation(content, "test", Enumerable.Range(0, count).Select(i => new KnowledgeChunk(Guid.NewGuid(),
            document.Id, 1, i, "text", i * 4, i * 4 + 4, new string('a', 64), Now)).ToArray(), Now);
        return document;
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class CapturingLogger : ILogger<RagRetriever>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private sealed class Dependencies : IEmbeddingService, IVectorStore, IKnowledgeChunkReader, IKnowledgeRepository
    {
        public EmbeddingProfile Profile { get; } = new("test-space", 3);
        public IReadOnlyList<KnowledgeVectorMatch> Matches { get; set; } = [];
        public IReadOnlyList<KnowledgeChunkDto> Current { get; set; } = [];
        public KnowledgeDocument? Document { get; set; }
        public IReadOnlyList<KnowledgeVector>? Stored { get; private set; }
        public bool FailEmbedding { get; set; }
        public bool FailStorage { get; set; }
        public Action? AfterEmbedding { get; set; }
        public List<CancellationToken> Tokens { get; } = [];
        public List<EmbeddingPurpose> Purposes { get; } = [];
        public List<string> Texts { get; } = [];
        public List<int> BatchSizes { get; } = [];
        public int SearchLimit { get; private set; }
        public int SearchCalls { get; private set; }
        public int Writes { get; private set; }
        public CapturingLogger Log { get; } = new();
        public IRagRetriever Retriever() => new RagRetriever(this, this, this, Log);
        public IRagIndexer Indexer() => new RagIndexer(this, this, this, new FixedClock(), NullLogger<RagIndexer>.Instance);
        public Task<IReadOnlyList<TextEmbedding>> GenerateAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken); Texts.AddRange(texts); Purposes.Add(purpose); BatchSizes.Add(texts.Count);
            if (FailEmbedding) throw new RagUnavailableException("Test provider failure");
            AfterEmbedding?.Invoke();
            return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts.Select(_ => new TextEmbedding(Profile.Id, [1,0,0])).ToArray());
        }
        public Task ReplaceDocumentAsync(Guid id, int version, EmbeddingProfile profile, IReadOnlyList<KnowledgeVector> vectors, CancellationToken cancellationToken = default)
        { Tokens.Add(cancellationToken); if (FailStorage) throw new ConflictException("Test stale document"); Writes++; Stored = vectors; return Task.CompletedTask; }
        public Task<IReadOnlyList<KnowledgeVectorMatch>> SearchAsync(TextEmbedding query, int limit, double minimumScore, KnowledgeVectorFilter? filter = null, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Tokens.Add(cancellationToken); SearchCalls++; SearchLimit = limit; return Task.FromResult(Matches); }
        public Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<KnowledgeChunkDto>> ReadCurrentAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
        { Tokens.Add(cancellationToken); return Task.FromResult(Current); }
        public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        { Tokens.Add(cancellationToken); return Task.FromResult(Document); }
        public Task<KnowledgePage> GetPageAsync(GetKnowledgePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool?> IsAuthorActiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(KnowledgeDocument document) => throw new NotSupportedException();
        public void Remove(KnowledgeDocument document) => throw new NotSupportedException();
    }
}
