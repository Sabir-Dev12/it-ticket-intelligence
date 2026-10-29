using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.ML;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public sealed class TicketPriorityPredictor : ITicketPriorityPredictor, IDisposable
{
    private readonly PredictionEngine<PriorityModelInput, PriorityModelOutput> engine;
    private readonly PriorityModelMetadata metadata;
    private readonly SemaphoreSlim gate = new(1, 1);

    public TicketPriorityPredictor(string modelDirectory)
    {
        var loaded = PriorityModelStore.Load(modelDirectory);
        metadata = loaded.Metadata;
        engine = loaded.Context.Model.CreatePredictionEngine<PriorityModelInput, PriorityModelOutput>(loaded.Model);
        ValidateOutput(engine.Predict(new PriorityModelInput { Title = "Startup verification", Description = "Verify the loaded priority classifier.", Category = "Other" }));
    }

    public async Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = engine.Predict(new PriorityModelInput { Title = input.Title, Description = input.Description, Category = input.Category.ToString() });
            var result = ValidateOutput(output);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }

    private TicketPriorityPrediction ValidateOutput(PriorityModelOutput output)
    {
        if (output.Score.Length != metadata.Labels.Length || output.Score.Any(score => !float.IsFinite(score) || score < 0 || score > 1) ||
            Math.Abs(output.Score.Sum(score => (double)score) - 1) > 0.001)
            throw new InvalidDataException("Priority model must return a finite normalized distribution.");
        var index = Array.IndexOf(output.Score, output.Score.Max());
        if (output.PredictedPriority != metadata.Labels[index]) throw new InvalidDataException("Priority prediction and score slots disagree.");
        return new TicketPriorityPrediction(Enum.Parse<TicketPriority>(metadata.Labels[index]), output.Score[index], metadata.ModelVersion);
    }

    public void Dispose() { engine.Dispose(); gate.Dispose(); }
}
