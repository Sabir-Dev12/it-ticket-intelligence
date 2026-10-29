using System.Diagnostics;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using Microsoft.Extensions.Logging;

namespace ITTicketIntelligence.Application.Knowledge.Retrieval;

public sealed class RagIndexer(IKnowledgeRepository repository, IEmbeddingService embeddings, IVectorStore vectors,
    TimeProvider clock, ILogger<RagIndexer> logger) : IRagIndexer
{
    public async Task<RagIndexResult> IndexDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Id(documentId).ThrowIfInvalid();
        var document = await repository.GetByIdAsync(documentId, cancellationToken) ?? throw new ResourceNotFoundException("Document");
        if (document.ProcessedAt is null || document.Chunks.Count == 0 || document.Chunks.Any(c => c.DocumentVersion != document.Version))
            throw new ConflictException("Prepare the current document through the Knowledge API before indexing it.");
        var snapshot = document.ToDto();
        var profile = embeddings.Profile;
        var watch = Stopwatch.StartNew();
        var records = new List<KnowledgeVector>(snapshot.Chunks.Count);
        foreach (var batch in snapshot.Chunks.Chunk(16))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await embeddings.GenerateAsync(batch.Select(c => snapshot.Title + "\n" + c.Content).ToArray(),
                EmbeddingPurpose.Document, cancellationToken);
            if (result.Count != batch.Length) throw new RagUnavailableException("The embedding provider returned an incomplete batch.");
            for (var i = 0; i < batch.Length; i++)
                records.Add(new KnowledgeVector(batch[i].Id, batch[i].Content, batch[i].Metadata,
                    new TextEmbedding(profile.Id, EmbeddingValidation.Normalize(result[i], profile))));
        }
        cancellationToken.ThrowIfCancellationRequested();
        await vectors.ReplaceDocumentAsync(snapshot.Id, snapshot.Version, profile, records, cancellationToken);
        logger.LogInformation("RAG indexed document {DocumentId} version {Version}: {Count} chunks, profile {Profile}, {ElapsedMs} ms",
            snapshot.Id, snapshot.Version, records.Count, profile.Id, watch.ElapsedMilliseconds);
        return new(snapshot.Id, snapshot.Version, records.Count, profile.Id, profile.Dimensions, clock.GetUtcNow());
    }
}
