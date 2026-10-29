using ITTicketIntelligence.Application.Intelligence;
using Microsoft.ML;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public sealed class TicketTeamPredictor : ITicketTeamPredictor, IDisposable
{
    private readonly PredictionEngine<TeamModelInput, TeamModelOutput> engine;
    private readonly TeamModelMetadata metadata;
    private readonly TeamRecommendationPolicy policy;
    private readonly SemaphoreSlim gate = new(1, 1);

    public TicketTeamPredictor(string directory, TeamRecommendationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        this.policy = policy;
        var loaded = TeamModelStore.Load(directory);
        metadata = loaded.Metadata;
        engine = loaded.Context.Model.CreatePredictionEngine<TeamModelInput, TeamModelOutput>(loaded.Model);
        try { ValidateOutput(engine.Predict(new TeamModelInput { Title = "Startup verification", Description = "Check the loaded team model." })); }
        catch { engine.Dispose(); gate.Dispose(); throw; }
    }

    public async Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ValidateOutput(engine.Predict(new TeamModelInput { Title = input.Title, Description = input.Description }));
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }

    private TeamRecommendation ValidateOutput(TeamModelOutput output)
    {
        if (output.Score.Length != metadata.Labels.Length || output.Score.Any(score => !float.IsFinite(score) || score < 0 || score > 1) ||
            Math.Abs(output.Score.Sum(score => (double)score) - 1) > 0.001) throw new InvalidDataException("Team scores must form a finite normalized distribution.");
        var index = Array.IndexOf(output.Score, output.Score.Max());
        if (output.PredictedTeam != metadata.Labels[index]) throw new InvalidDataException("Team prediction and score slots disagree.");
        return policy.Apply(metadata.Labels[index], output.Score[index], metadata.ModelVersion);
    }

    public void Dispose() { engine.Dispose(); gate.Dispose(); }
}
