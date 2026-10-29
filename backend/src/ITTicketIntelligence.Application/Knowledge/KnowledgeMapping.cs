using ITTicketIntelligence.Domain.Knowledge;

namespace ITTicketIntelligence.Application.Knowledge;

internal static class KnowledgeMapping
{
    public static KnowledgeDocumentDto ToDto(this KnowledgeDocument document) => new(document.Id, document.Title,
        document.Content, document.Source, document.Category, document.Kind, document.Format, document.Version,
        document.CreatedById, document.CreatedAt, document.UpdatedAt, document.CleanedContent,
        document.ProcessingVersion, document.ProcessedAt,
        document.Chunks.OrderBy(chunk => chunk.Position).Select(chunk => new KnowledgeChunkDto(chunk.Id, chunk.Content,
            new KnowledgeChunkMetadata(document.Id, chunk.DocumentVersion, document.Title, document.Source, document.Category,
                document.Kind, document.Format, chunk.Position, chunk.StartOffset, chunk.EndOffset, chunk.ContentHash))).ToList());
}
