namespace ITTicketIntelligence.Infrastructure.MachineLearning;

// Category is used only by the training label encoder, never by the text featurizer.
// Inference leaves it empty; the fitted classifier does not use the Label column to score.
public sealed class CategoryModelInput
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class CategoryModelOutput
{
    public string PredictedCategory { get; set; } = "";
    public float[] Score { get; set; } = [];
}

public sealed record CategoryModelMetadata(
    int ContractVersion, string ModelVersion, string Trainer, string MlNetVersion,
    string DatasetVersion, DateTimeOffset TrainedAtUtc, string[] Features, string[] Labels,
    string ModelSha256, Dictionary<string, string> DatasetSha256);
