using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Knowledge;

public sealed record CreateKnowledgeCommand(string Title, string Content, string Source, TicketCategory Category,
    KnowledgeDocumentKind Kind, KnowledgeContentFormat Format, Guid? CreatedById = null);
public sealed record UpdateKnowledgeCommand(Guid Id, string Title, string Content, string Source, TicketCategory Category,
    KnowledgeDocumentKind Kind, KnowledgeContentFormat Format, int Version);
public sealed record DeleteKnowledgeCommand(Guid Id, int Version);
public sealed record GetKnowledgeQuery(Guid Id);
public sealed record GetKnowledgePageQuery(int Page = 1, int PageSize = 20, string? Search = null,
    TicketCategory? Category = null, KnowledgeDocumentKind? Kind = null);

public sealed record KnowledgeChunkMetadata(Guid DocumentId, int DocumentVersion, string Title, string Source,
    TicketCategory Category, KnowledgeDocumentKind Kind, KnowledgeContentFormat Format,
    int Position, int StartOffset, int EndOffset, string ContentHash);
public sealed record KnowledgeChunkDto(Guid Id, string Content, KnowledgeChunkMetadata Metadata);
public sealed record KnowledgeDocumentDto(Guid Id, string Title, string Content, string Source, TicketCategory Category,
    KnowledgeDocumentKind Kind, KnowledgeContentFormat Format, int Version, Guid? CreatedById,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? CleanedContent, string? ProcessingVersion,
    DateTimeOffset? ProcessedAt, IReadOnlyList<KnowledgeChunkDto> Chunks);
public sealed record KnowledgeSummaryDto(Guid Id, string Title, string Source, TicketCategory Category,
    KnowledgeDocumentKind Kind, KnowledgeContentFormat Format, int Version, DateTimeOffset UpdatedAt, int ChunkCount);
public sealed record KnowledgePage(IReadOnlyList<KnowledgeSummaryDto> Items, int TotalCount, int Page, int PageSize);
