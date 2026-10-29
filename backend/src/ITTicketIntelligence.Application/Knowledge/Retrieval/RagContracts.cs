namespace ITTicketIntelligence.Application.Knowledge.Retrieval;

public sealed record RagQuery(string Question, int Limit = 5, double MinimumScore = 0.35, KnowledgeVectorFilter? Filter = null);
public sealed record RetrievedChunk(Guid DocumentId, string Title, Guid ChunkId, string Chunk, double RelevanceScore,
    KnowledgeChunkMetadata Metadata);
public sealed record RetrievedContext(string EmbeddingProfile, IReadOnlyList<RetrievedChunk> Results);
public sealed record RagIndexResult(Guid DocumentId, int DocumentVersion, int ChunkCount, string EmbeddingProfile,
    int Dimensions, DateTimeOffset IndexedAt);

public interface IRagRetriever
{
    Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default);
}
public interface IRagIndexer
{
    Task<RagIndexResult> IndexDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
public interface IKnowledgeChunkReader
{
    Task<IReadOnlyList<KnowledgeChunkDto>> ReadCurrentAsync(IReadOnlyList<Guid> chunkIds, CancellationToken cancellationToken = default);
}

public sealed class RagUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

public static class EmbeddingValidation
{
    public static float[] Normalize(TextEmbedding embedding, EmbeddingProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 256 || profile.Dimensions is < 1 or > 4096 ||
            embedding.Model != profile.Id || embedding.Values is null || embedding.Values.Count != profile.Dimensions ||
            embedding.Values.Any(value => !float.IsFinite(value)))
            throw new RagUnavailableException("The embedding provider returned an incompatible vector.");
        var norm = Math.Sqrt(embedding.Values.Sum(value => (double)value * value));
        if (!double.IsFinite(norm) || norm <= 0) throw new RagUnavailableException("The embedding provider returned an empty vector.");
        return embedding.Values.Select(value => (float)(value / norm)).ToArray();
    }
}
