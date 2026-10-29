using ITTicketIntelligence.Application.Tickets.Validation;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Intelligence;

public interface ITicketPriorityPredictor
{
    Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default);
}

public sealed record TicketPriorityInput
{
    public TicketPriorityInput(string title, string description, TicketCategory category)
    {
        new TicketValidation().Text(title, "title", 160).Text(description, "description", 8000)
            .Enum(category, "category").ThrowIfInvalid();
        Title = title.Trim();
        Description = description.Trim();
        Category = category;
    }

    public string Title { get; }
    public string Description { get; }
    public TicketCategory Category { get; }
}

public sealed record TicketPriorityPrediction(TicketPriority PredictedPriority, double Confidence, string ModelVersion);
