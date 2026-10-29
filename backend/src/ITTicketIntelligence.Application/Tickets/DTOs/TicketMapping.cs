using ITTicketIntelligence.Domain.Tickets;

namespace ITTicketIntelligence.Application.Tickets.DTOs;

internal static class TicketMapping
{
    public static TicketDto ToDto(this Ticket ticket) => new(ticket.Id, ticket.Reference, ticket.Title,
        ticket.Description, ticket.CreatedById, ticket.Status, ticket.Category, ticket.Priority,
        ticket.AssignedTeamId, ticket.Revision, ticket.CreatedAt, ticket.UpdatedAt, ticket.Resolution,
        ticket.ResolvedAt, ticket.ClosedAt, ticket.IsArchived, ticket.ArchivedAt,
        ticket.Comments.OrderBy(comment => comment.CreatedAt).ThenBy(comment => comment.Id).Select(ToDto).ToList(),
        ticket.Predictions.OrderByDescending(prediction => prediction.CreatedAt).ThenBy(prediction => prediction.Id)
            .Select(prediction => new TicketPredictionDto(prediction.Id, prediction.Type, prediction.Confidence.Value,
                prediction.ModelVersion, prediction.InputRevision, prediction.PredictedCategory,
                prediction.PredictedPriority, prediction.RecommendedTeamId, prediction.CreatedAt, prediction.AnalysisId,
                prediction.RecommendedTeamName, prediction.NeedsHumanReview, prediction.ReviewThreshold)).ToList(), ticket.AssignedUserId);

    public static TicketCommentDto ToDto(this TicketComment comment) =>
        new(comment.Id, comment.TicketId, comment.AuthorId, comment.Message, comment.CreatedAt);
}
