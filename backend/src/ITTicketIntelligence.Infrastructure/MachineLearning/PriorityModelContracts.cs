namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public sealed class PriorityModelInput
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    // Training-only target and weight. Neither is a feature; inference uses defaults.
    public string Priority { get; set; } = "";
    public float ExampleWeight { get; set; } = 1;
}

public sealed class PriorityModelOutput
{
    public string PredictedPriority { get; set; } = "";
    public float[] Score { get; set; } = [];
}

public sealed record PriorityModelMetadata(int ContractVersion, string ModelVersion, string Trainer, string MlNetVersion,
    string DatasetVersion, DateTimeOffset TrainedAtUtc, string[] Features, string[] Labels, string CategoryInputPolicy,
    bool UseClassWeights, float L2Regularization, Dictionary<string, float> ClassWeights,
    string ModelSha256, Dictionary<string, string> DatasetSha256);
