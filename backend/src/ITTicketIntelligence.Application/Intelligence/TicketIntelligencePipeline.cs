using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Validation;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.Application.Realtime;

namespace ITTicketIntelligence.Application.Intelligence;

public sealed class TicketIntelligencePipeline(ITicketIntelligenceStore store,
    ITicketCategoryPredictor categoryPredictor, ITicketPriorityPredictor priorityPredictor,
    ITicketTeamPredictor teamPredictor, IntelligenceReviewPolicy reviewPolicy, TimeProvider timeProvider, TicketEventNotifier notifications)
    : ITicketIntelligencePipeline
{
    public async Task<IntelligenceResult> AnalyzeAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Id(ticketId, "ticketId").ThrowIfInvalid();
        var input = await store.GetInputAsync(ticketId, cancellationToken)
            ?? throw new ResourceNotFoundException("Ticket");
        if (input.IsArchived || input.Status is not (TicketStatus.Open or TicketStatus.InProgress))
            throw new ConflictException("Only an open or in-progress, unarchived ticket can be analyzed.");

        var category = await categoryPredictor.PredictAsync(new TicketCategoryInput(input.Title, input.Description), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var priority = await priorityPredictor.PredictAsync(new TicketPriorityInput(input.Title, input.Description, category.Category), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var team = await teamPredictor.PredictAsync(new TicketTeamInput(input.Title, input.Description), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Predictor ports can be replaced. Reject inconsistent outputs before appending any history.
        if (team.Team is not null && !TicketTeamNames.All.Contains(team.Team, StringComparer.Ordinal))
            throw new InvalidOperationException("The team predictor returned an unknown team label.");
        if (team.NeedsHumanReview != (team.Confidence < team.Threshold) ||
            (team.Team is null) != team.NeedsHumanReview)
            throw new InvalidOperationException("The team predictor returned an inconsistent abstention decision.");

        var analysisId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        TicketPrediction[] predictions =
        [
            TicketPrediction.ForCategory(Guid.NewGuid(), ticketId, category.Category, new PredictionConfidence(category.Confidence),
                category.ModelVersion, input.Revision, now, analysisId, reviewPolicy.CategoryThreshold),
            TicketPrediction.ForPriority(Guid.NewGuid(), ticketId, priority.PredictedPriority, new PredictionConfidence(priority.Confidence),
                priority.ModelVersion, input.Revision, now, analysisId, reviewPolicy.PriorityThreshold),
            TicketPrediction.ForTeamRecommendation(Guid.NewGuid(), ticketId, team.Team, new PredictionConfidence(team.Confidence),
                team.ModelVersion, input.Revision, now, analysisId, team.Threshold)
        ];
        var result = new IntelligenceResult(category.Category, category.Confidence, priority.PredictedPriority,
            priority.Confidence, team.Team, team.Confidence, predictions.Any(prediction => prediction.NeedsHumanReview));

        await store.SaveAsync(ticketId, input.Revision, predictions, cancellationToken);
        await notifications.PublishAsync(TicketEventName.PredictionCompleted, ticketId, input.Revision, now, analysisId);
        return result;
    }
}
