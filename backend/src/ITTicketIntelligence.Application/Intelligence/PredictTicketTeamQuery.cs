using ITTicketIntelligence.Application.Common;

namespace ITTicketIntelligence.Application.Intelligence;

public sealed record PredictTicketTeamQuery(string Title, string Description);

public sealed class PredictTicketTeamHandler(ITicketTeamPredictor predictor) : IQueryHandler<PredictTicketTeamQuery, TeamRecommendation>
{
    public Task<TeamRecommendation> HandleAsync(PredictTicketTeamQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return predictor.PredictAsync(new TicketTeamInput(query.Title, query.Description), cancellationToken);
    }
}
