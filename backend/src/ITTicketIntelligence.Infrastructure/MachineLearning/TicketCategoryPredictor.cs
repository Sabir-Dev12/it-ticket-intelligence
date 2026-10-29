using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.ML;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public sealed class TicketCategoryPredictor : ITicketCategoryPredictor, IDisposable
{
    private readonly PredictionEngine<CategoryModelInput, CategoryModelOutput> engine;
    private readonly CategoryModelMetadata metadata;
    // PredictionEngine is mutable and not thread safe: serialize access to this loaded instance.
    private readonly SemaphoreSlim gate = new(1, 1);

    public TicketCategoryPredictor(string modelDirectory)
    {
        var loaded = CategoryModelStore.Load(modelDirectory);
        metadata = loaded.Metadata;
        engine = loaded.Context.Model.CreatePredictionEngine<CategoryModelInput, CategoryModelOutput>(loaded.Model);
        // Exercise the complete deserialized pipeline before the host accepts requests.
        ValidateOutput(engine.Predict(new CategoryModelInput { Title = "Startup verification", Description = "Verify the loaded category classifier." }));
    }

    public async Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = engine.Predict(new CategoryModelInput { Title = input.Title, Description = input.Description });
            var result = ValidateOutput(output);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }

    private TicketCategoryPrediction ValidateOutput(CategoryModelOutput output)
    {
        if (output.Score.Length != metadata.Labels.Length || output.Score.Any(score => !float.IsFinite(score) || score < 0 || score > 1) ||
            Math.Abs(output.Score.Sum(score => (double)score) - 1) > 0.001)
            throw new InvalidDataException("Category model must return a finite normalized score distribution.");
        var index = Array.IndexOf(output.Score, output.Score.Max());
        if (output.PredictedCategory != metadata.Labels[index])
            throw new InvalidDataException("Category prediction and score slots disagree.");
        return new TicketCategoryPrediction(Enum.Parse<TicketCategory>(metadata.Labels[index]), output.Score[index], metadata.ModelVersion);
    }

    public void Dispose() { engine.Dispose(); gate.Dispose(); }
}
