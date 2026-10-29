using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.ValueObjects;

namespace ITTicketIntelligence.Domain.Tickets;

public sealed class TicketPrediction : Entity
{
    private TicketPrediction(Guid id, Guid ticketId, PredictionType type, PredictionConfidence confidence,
        string modelVersion, int inputRevision, DateTimeOffset createdAt,
        TicketCategory? predictedCategory = null, TicketPriority? predictedPriority = null, Guid? recommendedTeamId = null,
        Guid? analysisId = null, double? reviewThreshold = null, string? recommendedTeamName = null) : base(id)
    {
        TicketId = Guard.Id(ticketId, nameof(ticketId));
        Type = type;
        Confidence = confidence;
        ModelVersion = Guard.RequiredText(modelVersion, nameof(modelVersion), 100);
        if (inputRevision < 1)
        {
            throw new DomainException("A prediction must refer to a positive ticket revision.");
        }

        InputRevision = inputRevision;
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        PredictedCategory = predictedCategory;
        PredictedPriority = predictedPriority;
        RecommendedTeamId = recommendedTeamId;
        AnalysisId = analysisId.HasValue ? Guard.Id(analysisId.Value, nameof(analysisId)) : null;
        if (reviewThreshold.HasValue && (!double.IsFinite(reviewThreshold.Value) || reviewThreshold <= 0 || reviewThreshold > 1))
            throw new DomainException("A review threshold must be finite and in (0, 1].");
        ReviewThreshold = reviewThreshold;
        NeedsHumanReview = reviewThreshold.HasValue && confidence.Value < reviewThreshold.Value;
        RecommendedTeamName = recommendedTeamName;
    }

    public Guid TicketId { get; }
    public PredictionType Type { get; }
    public PredictionConfidence Confidence { get; }
    public string ModelVersion { get; }
    public int InputRevision { get; }
    public TicketCategory? PredictedCategory { get; }
    public TicketPriority? PredictedPriority { get; }
    public Guid? RecommendedTeamId { get; }
    public DateTimeOffset CreatedAt { get; }
    public Guid? AnalysisId { get; }
    public double? ReviewThreshold { get; }
    public bool NeedsHumanReview { get; }
    public string? RecommendedTeamName { get; }

    public static TicketPrediction ForCategory(Guid id, Guid ticketId, TicketCategory category,
        PredictionConfidence confidence, string modelVersion, int inputRevision, DateTimeOffset createdAt,
        Guid? analysisId = null, double? reviewThreshold = null)
    {
        return new TicketPrediction(id, ticketId, PredictionType.Category, confidence, modelVersion, inputRevision,
            createdAt, predictedCategory: Guard.Defined(category, nameof(category)), analysisId: analysisId, reviewThreshold: reviewThreshold);
    }

    public static TicketPrediction ForPriority(Guid id, Guid ticketId, TicketPriority priority,
        PredictionConfidence confidence, string modelVersion, int inputRevision, DateTimeOffset createdAt,
        Guid? analysisId = null, double? reviewThreshold = null)
    {
        return new TicketPrediction(id, ticketId, PredictionType.Priority, confidence, modelVersion, inputRevision,
            createdAt, predictedPriority: Guard.Defined(priority, nameof(priority)), analysisId: analysisId, reviewThreshold: reviewThreshold);
    }

    public static TicketPrediction ForTeamRouting(Guid id, Guid ticketId, Guid teamId,
        PredictionConfidence confidence, string modelVersion, int inputRevision, DateTimeOffset createdAt)
    {
        return new TicketPrediction(id, ticketId, PredictionType.TeamRouting, confidence, modelVersion, inputRevision,
            createdAt, recommendedTeamId: Guard.Id(teamId, nameof(teamId)));
    }

    public static TicketPrediction ForTeamRecommendation(Guid id, Guid ticketId, string? teamName,
        PredictionConfidence confidence, string modelVersion, int inputRevision, DateTimeOffset createdAt,
        Guid analysisId, double reviewThreshold)
    {
        var prediction = new TicketPrediction(id, ticketId, PredictionType.TeamRouting, confidence, modelVersion,
            inputRevision, createdAt, analysisId: analysisId, reviewThreshold: reviewThreshold,
            recommendedTeamName: teamName is null ? null : Guard.RequiredText(teamName, nameof(teamName), 32));
        if ((teamName is null) != prediction.NeedsHumanReview)
            throw new DomainException("A team must be withheld below the confidence threshold and provided otherwise.");
        return prediction;
    }
}
