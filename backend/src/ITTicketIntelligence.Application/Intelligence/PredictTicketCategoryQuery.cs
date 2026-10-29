using ITTicketIntelligence.Application.Common;

namespace ITTicketIntelligence.Application.Intelligence;

public sealed record PredictTicketCategoryQuery(string Title, string Description);

public sealed class PredictTicketCategoryHandler(ITicketCategoryPredictor predictor)
    : IQueryHandler<PredictTicketCategoryQuery, TicketCategoryPrediction>
{
    public Task<TicketCategoryPrediction> HandleAsync(PredictTicketCategoryQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return predictor.PredictAsync(new TicketCategoryInput(query.Title, query.Description), cancellationToken);
    }
}
