using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Tickets.DTOs;

public sealed record TicketSummaryDto(Guid Id, string Reference, string Title, string Description, Guid CreatedById,
    TicketStatus Status, TicketCategory? Category, TicketPriority? Priority, Guid? AssignedTeamId, int Revision,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsArchived, DateTimeOffset? ArchivedAt,
    int CommentCount, int PredictionCount);

public sealed record TicketDto(Guid Id, string Reference, string Title, string Description, Guid CreatedById,
    TicketStatus Status, TicketCategory? Category, TicketPriority? Priority, Guid? AssignedTeamId, int Revision,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt, bool IsArchived, DateTimeOffset? ArchivedAt,
    IReadOnlyList<TicketCommentDto> Comments, IReadOnlyList<TicketPredictionDto> Predictions, Guid? AssignedUserId = null, string? AssignedUserName = null);

public sealed record TicketCommentDto(Guid Id, Guid TicketId, Guid AuthorId, string Message, DateTimeOffset CreatedAt);

public sealed record TicketPredictionDto(Guid Id, PredictionType Type, double Confidence, string ModelVersion,
    int InputRevision, TicketCategory? PredictedCategory, TicketPriority? PredictedPriority,
    Guid? RecommendedTeamId, DateTimeOffset CreatedAt, Guid? AnalysisId = null,
    string? RecommendedTeamName = null, bool NeedsHumanReview = false, double? ReviewThreshold = null);

public sealed record AddCommentResult(TicketCommentDto Comment, int TicketRevision);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
