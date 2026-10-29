using ITTicketIntelligence.Application.Tickets.Validation;

namespace ITTicketIntelligence.Application.Intelligence;

public interface ITicketTeamPredictor
{
    Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default);
}

public sealed record TicketTeamInput
{
    public TicketTeamInput(string title, string description)
    {
        new TicketValidation().Text(title, "title", 160).Text(description, "description", 8000).ThrowIfInvalid();
        Title = title.Trim();
        Description = description.Trim();
    }
    public string Title { get; }
    public string Description { get; }
}

// Model taxonomy, deliberately separate from persisted Team identities and memberships.
public static class TicketTeamNames
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "Support", "Network", "Database", "Security", "Backend", "Infrastructure" });
}

public sealed record TeamRecommendation(string? Team, double Confidence, bool NeedsHumanReview, double Threshold, string ModelVersion);

public sealed class TeamRecommendationPolicy
{
    public const double DefaultThreshold = 0.8;

    public TeamRecommendationPolicy(double threshold = DefaultThreshold)
    {
        if (!double.IsFinite(threshold) || threshold <= 0 || threshold > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "The team confidence threshold must be finite and in (0, 1].");
        Threshold = threshold;
    }

    public double Threshold { get; }

    public TeamRecommendation Apply(string candidateTeam, double confidence, string modelVersion)
    {
        if (!TicketTeamNames.All.Contains(candidateTeam, StringComparer.Ordinal)) throw new ArgumentException("Unknown team label.", nameof(candidateTeam));
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        var review = confidence < Threshold;
        return new TeamRecommendation(review ? null : candidateTeam, confidence, review, Threshold, modelVersion);
    }
}
