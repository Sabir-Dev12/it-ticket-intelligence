namespace ITTicketIntelligence.Application.Knowledge.Retrieval;

public sealed record TextEmbedding(string Model, IReadOnlyList<float> Values);

public enum EmbeddingPurpose { Document, Query }
public sealed record EmbeddingProfile(string Id, int Dimensions);

/// <summary>Adapters preserve input order and identify an immutable model/prompt space.</summary>
public interface IEmbeddingService
{
    EmbeddingProfile Profile { get; }
    Task<IReadOnlyList<TextEmbedding>> GenerateAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeVector(Guid ChunkId, string Content, KnowledgeChunkMetadata Metadata, TextEmbedding Embedding);
public sealed record KnowledgeVectorMatch(Guid ChunkId, KnowledgeChunkMetadata Metadata, double Score);
public sealed record KnowledgeVectorFilter(Guid? DocumentId = null,
    ITTicketIntelligence.Domain.Enums.TicketCategory? Category = null,
    ITTicketIntelligence.Domain.Enums.KnowledgeDocumentKind? Kind = null);

/// <summary>Replace a complete document/profile atomically; reject obsolete document versions.</summary>
public interface IVectorStore
{
    Task ReplaceDocumentAsync(Guid documentId, int documentVersion, EmbeddingProfile profile, IReadOnlyList<KnowledgeVector> vectors,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeVectorMatch>> SearchAsync(TextEmbedding query, int limit, double minimumScore,
        KnowledgeVectorFilter? filter = null,
        CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
