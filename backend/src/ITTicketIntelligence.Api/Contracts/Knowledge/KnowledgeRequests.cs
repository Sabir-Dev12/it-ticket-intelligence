using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Api.Contracts.Knowledge;

public sealed record CreateKnowledgeRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(100000)] public required string Content { get; init; }
    [Required, StringLength(512)] public required string Source { get; init; }
    public TicketCategory Category { get; init; } = TicketCategory.Other;
    public KnowledgeDocumentKind Kind { get; init; } = KnowledgeDocumentKind.Document;
    public KnowledgeContentFormat Format { get; init; } = KnowledgeContentFormat.PlainText;
    public Guid? CreatedById { get; init; }
}

public sealed record UpdateKnowledgeRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(100000)] public required string Content { get; init; }
    [Required, StringLength(512)] public required string Source { get; init; }
    public required TicketCategory Category { get; init; }
    public required KnowledgeDocumentKind Kind { get; init; }
    public required KnowledgeContentFormat Format { get; init; }
    [Range(1, int.MaxValue)] public required int Version { get; init; }
}

public sealed record GetKnowledgeRequest
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public TicketCategory? Category { get; init; }
    public KnowledgeDocumentKind? Kind { get; init; }
}
