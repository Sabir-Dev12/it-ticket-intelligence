using ITTicketIntelligence.Application.Tickets.Validation;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Intelligence;

public interface ITicketCategoryPredictor
{
    Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default);
}

public sealed record TicketCategoryInput
{
    public TicketCategoryInput(string title, string description)
    {
        new TicketValidation().Text(title, "title", 160).Text(description, "description", 8000).ThrowIfInvalid();
        Title = title.Trim();
        Description = description.Trim();
    }

    public string Title { get; }
    public string Description { get; }
}

public sealed record TicketCategoryPrediction(TicketCategory Category, double Confidence, string ModelVersion);
