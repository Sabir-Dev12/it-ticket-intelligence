using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ITTicketIntelligence.Application.Knowledge.Retrieval;

public sealed class RagRetriever(IEmbeddingService embeddings, IVectorStore vectors, IKnowledgeChunkReader source,
    ILogger<RagRetriever> logger) : IRagRetriever
{
    public async Task<RetrievedContext> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = new KnowledgeValidation()
            .Require(!string.IsNullOrWhiteSpace(query.Question) && query.Question.Length <= 2000 && !query.Question.Contains('\0'), "question", "Question obligatoire sans caractère NUL, maximum 2000 caractères.")
            .Require(query.Limit is >= 1 and <= 20, "limit", "Limite entre 1 et 20.")
            .Require(double.IsFinite(query.MinimumScore) && query.MinimumScore is >= -1 and <= 1, "minimumScore", "Score cosinus entre -1 et 1.")
            .Require(query.Filter?.DocumentId != Guid.Empty, "documentId", "Identifiant de document non vide.")
            .Require(query.Filter?.Category is not { } category || Enum.IsDefined(category), "category", "Catégorie non reconnue.")
            .Require(query.Filter?.Kind is not { } kind || Enum.IsDefined(kind), "kind", "Type non reconnu.");
        validation.ThrowIfInvalid();

        var watch = Stopwatch.StartNew();
        var profile = embeddings.Profile;
        var generated = await embeddings.GenerateAsync([query.Question.Trim()], EmbeddingPurpose.Query, cancellationToken);
        if (generated.Count != 1) throw new RagUnavailableException("The embedding provider returned an invalid query batch.");
        var vector = new TextEmbedding(profile.Id, EmbeddingValidation.Normalize(generated[0], profile));
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = await vectors.SearchAsync(vector, query.Limit * 4, query.MinimumScore, query.Filter, cancellationToken);
        if (candidates.Any(c => !double.IsFinite(c.Score) || c.Score is < -1 or > 1))
            throw new RagUnavailableException("The vector store returned an invalid cosine score.");
        var current = await source.ReadCurrentAsync(candidates.Select(c => c.ChunkId).Distinct().ToArray(), cancellationToken);
        var byId = current.ToDictionary(c => c.Id);
        var results = candidates.Where(c => c.Score >= query.MinimumScore)
            .OrderByDescending(c => c.Score).ThenBy(c => c.ChunkId).DistinctBy(c => c.ChunkId)
            .Where(c => byId.TryGetValue(c.ChunkId, out var chunk) &&
                chunk.Metadata.DocumentId == c.Metadata.DocumentId && chunk.Metadata.DocumentVersion == c.Metadata.DocumentVersion &&
                chunk.Metadata.ContentHash == c.Metadata.ContentHash && Matches(chunk.Metadata, query.Filter))
            .Take(query.Limit).Select(c =>
            {
                var chunk = byId[c.ChunkId];
                return new RetrievedChunk(chunk.Metadata.DocumentId, chunk.Metadata.Title, chunk.Id, chunk.Content, c.Score, chunk.Metadata);
            }).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("RAG retrieved {Count} chunks from {CandidateCount} candidates, profile {Profile}, {ElapsedMs} ms",
            results.Count, candidates.Count, profile.Id, watch.ElapsedMilliseconds);
        return new(profile.Id, results);
    }

    private static bool Matches(KnowledgeChunkMetadata metadata, KnowledgeVectorFilter? filter) => filter is null ||
        ((!filter.DocumentId.HasValue || filter.DocumentId == metadata.DocumentId) &&
         (!filter.Category.HasValue || filter.Category == metadata.Category) && (!filter.Kind.HasValue || filter.Kind == metadata.Kind));
}
