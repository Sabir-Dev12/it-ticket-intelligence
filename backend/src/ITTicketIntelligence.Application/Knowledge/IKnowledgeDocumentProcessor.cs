using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Knowledge;

public sealed record KnowledgeProcessingInput(Guid DocumentId, int DocumentVersion, string Title, string Content,
    string Source, TicketCategory Category, KnowledgeDocumentKind Kind, KnowledgeContentFormat Format);
public sealed record PreparedKnowledgeChunk(string Content, KnowledgeChunkMetadata Metadata);
public sealed record PreparedKnowledgeDocument(string CleanedContent, string ProcessingVersion,
    IReadOnlyList<PreparedKnowledgeChunk> Chunks);

public interface IKnowledgeDocumentProcessor
{
    Task<PreparedKnowledgeDocument> PrepareAsync(KnowledgeProcessingInput input, CancellationToken cancellationToken = default);
}

public interface IKnowledgeTextExtractor
{
    Task<string> ExtractAsync(string content, KnowledgeContentFormat format, CancellationToken cancellationToken = default);
}
