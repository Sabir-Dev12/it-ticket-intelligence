using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.Infrastructure.Persistence.Rag;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;

/// <summary>Exact pgvector cosine search with document permissions applied before ranking.</summary>
internal sealed class PostgresVectorStore(AppDbContext db, IKnowledgeAccessContext access) : IVectorStore
{
    public async Task ReplaceDocumentAsync(Guid documentId, int documentVersion, EmbeddingProfile profile,
        IReadOnlyList<KnowledgeVector> vectors, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (documentId == Guid.Empty || documentVersion < 1 || vectors.Count == 0 || vectors.Select(v => v.ChunkId).Distinct().Count() != vectors.Count)
            throw new ArgumentException("Indexing requires a complete nonempty set of unique chunks.");
        var normalized = vectors.Select(v => EmbeddingValidation.Normalize(v.Embedding, profile)).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var document = await db.KnowledgeDocuments.FromSqlInterpolated($"SELECT *, xmin FROM app.knowledge_documents WHERE id = {documentId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException("Document");
        if (document.Version != documentVersion || document.ProcessedAt is null)
            throw new ConflictException("The document changed during indexing. Prepare and index the current version.");
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(c => c.DocumentId == documentId).ToDictionaryAsync(c => c.Id, cancellationToken);
        if (chunks.Count != vectors.Count || vectors.Any(v => !chunks.TryGetValue(v.ChunkId, out var c) ||
            c.DocumentVersion != documentVersion || v.Metadata.DocumentId != documentId || v.Metadata.DocumentVersion != documentVersion ||
            v.Metadata.ContentHash != c.ContentHash || v.Content != c.Content))
            throw new ConflictException("The document's chunks changed during indexing.");
        var ids = chunks.Keys.ToArray();
        await db.RagVectors.Where(v => ids.Contains(v.ChunkId) && v.Profile == profile.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in db.ChangeTracker.Entries<RagVectorRecord>()
            .Where(entry => ids.Contains(entry.Entity.ChunkId) && entry.Entity.Profile == profile.Id).ToArray())
            entry.State = EntityState.Detached;
        var now = DateTimeOffset.UtcNow;
        db.RagVectors.AddRange(vectors.Select((v, i) => new RagVectorRecord
        { ChunkId = v.ChunkId, Profile = profile.Id, Dimensions = profile.Dimensions, Embedding = normalized[i], CreatedAt = now, UpdatedAt = now }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeVectorMatch>> SearchAsync(TextEmbedding query, int limit, double minimumScore,
        KnowledgeVectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 80 || !double.IsFinite(minimumScore) || minimumScore is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var values = EmbeddingValidation.Normalize(query, new(query.Model, query.Values.Count));
        const string sql = """
            SELECT c.id AS "ChunkId", d.id AS "DocumentId", c.document_version AS "DocumentVersion",
                d.title AS "Title", d.source AS "Source", d.category AS "Category", d.kind AS "Kind", d.format AS "Format",
                c.position AS "Position", c.start_offset AS "StartOffset", c.end_offset AS "EndOffset", c.content_hash AS "ContentHash",
                LEAST(1.0, GREATEST(-1.0, sim.score))::double precision AS "Score"
            FROM app.rag_vectors v
            JOIN app.knowledge_chunks c ON c.id = v.chunk_id
            JOIN app.knowledge_documents d ON d.id = c.document_id
            CROSS JOIN LATERAL (SELECT CASE WHEN v.dimensions=@dimensions THEN 1 - (v.embedding_vector <=> @embedding::vector) END AS score) sim
            WHERE v.profile = @profile AND v.dimensions = @dimensions AND c.document_version = d.version AND d.processed_at IS NOT NULL
                AND @can_search AND (NOT d.is_restricted OR @admin OR d.created_by_id=@caller)
                AND (@document IS NULL OR d.id = @document) AND (@category IS NULL OR d.category = @category)
                AND (@kind IS NULL OR d.kind = @kind) AND sim.score IS NOT NULL AND sim.score >= @minimum AND sim.score <= 1.000001
            ORDER BY "Score" DESC, c.id
            LIMIT @limit
            """;
        var rows = await db.Database.SqlQueryRaw<SearchRow>(sql,
            new NpgsqlParameter("can_search", access.CanSearch),new NpgsqlParameter("admin",access.IsAdmin),new NpgsqlParameter("caller",NpgsqlDbType.Uuid){Value=(object?)access.UserId??DBNull.Value},
            new NpgsqlParameter("embedding", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = values },
            new NpgsqlParameter("profile", query.Model), new NpgsqlParameter("dimensions", values.Length),
            new NpgsqlParameter("document", NpgsqlDbType.Uuid) { Value = (object?)filter?.DocumentId ?? DBNull.Value },
            new NpgsqlParameter("category", NpgsqlDbType.Text) { Value = (object?)filter?.Category?.ToString() ?? DBNull.Value },
            new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = (object?)filter?.Kind?.ToString() ?? DBNull.Value },
            new NpgsqlParameter("minimum", minimumScore), new NpgsqlParameter("limit", limit))
            .ToListAsync(cancellationToken);
        return rows.Select(row => new KnowledgeVectorMatch(row.ChunkId, new KnowledgeChunkMetadata(row.DocumentId,
            row.DocumentVersion, row.Title, row.Source, Enum.Parse<TicketCategory>(row.Category),
            Enum.Parse<KnowledgeDocumentKind>(row.Kind), Enum.Parse<KnowledgeContentFormat>(row.Format),
            row.Position, row.StartOffset, row.EndOffset, row.ContentHash), row.Score)).ToArray();
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var chunks = db.KnowledgeChunks.Where(c => c.DocumentId == documentId).Select(c => c.Id);
        await db.RagVectors.Where(v => chunks.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken);
    }

    private sealed class SearchRow
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public int DocumentVersion { get; set; }
        public string Title { get; set; } = "";
        public string Source { get; set; } = "";
        public string Category { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Format { get; set; } = "";
        public int Position { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public string ContentHash { get; set; } = "";
        public double Score { get; set; }
    }
}
