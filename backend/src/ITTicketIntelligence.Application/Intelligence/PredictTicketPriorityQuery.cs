using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Intelligence;

public sealed record PredictTicketPriorityQuery(string Title, string Description, TicketCategory Category);

public sealed class PredictTicketPriorityHandler(ITicketPriorityPredictor predictor)
    : IQueryHandler<PredictTicketPriorityQuery, TicketPriorityPrediction>
{
    public Task<TicketPriorityPrediction> HandleAsync(PredictTicketPriorityQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return predictor.PredictAsync(new TicketPriorityInput(query.Title, query.Description, query.Category), cancellationToken);
    }
}
