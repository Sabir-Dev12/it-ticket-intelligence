using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Intelligence;

public interface ITicketIntelligencePipeline
{
    Task<IntelligenceResult> AnalyzeAsync(Guid ticketId, CancellationToken cancellationToken = default);
}

public sealed record IntelligenceResult(TicketCategory Category, double CategoryConfidence,
    TicketPriority Priority, double PriorityConfidence, string? RecommendedTeam, double TeamConfidence,
    bool NeedsHumanReview);

/// <summary>Server-owned review thresholds; accepting a score never authorizes a ticket change.</summary>
public sealed class IntelligenceReviewPolicy
{
    public IntelligenceReviewPolicy(double categoryThreshold = 0.8, double priorityThreshold = 0.8)
    {
        Validate(categoryThreshold, nameof(categoryThreshold));
        Validate(priorityThreshold, nameof(priorityThreshold));
        CategoryThreshold = categoryThreshold;
        PriorityThreshold = priorityThreshold;
    }

    public double CategoryThreshold { get; }
    public double PriorityThreshold { get; }

    private static void Validate(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0 || value > 1)
            throw new ArgumentOutOfRangeException(name, "Review thresholds must be finite and in (0, 1].");
    }
}
