namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public sealed class TeamModelInput
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string RecommendedTeam { get; set; } = "";
    public float ExampleWeight { get; set; } = 1;
}

public sealed class TeamModelOutput
{
    public string PredictedTeam { get; set; } = "";
    public float[] Score { get; set; } = [];
}

public sealed record TeamModelMetadata(int ContractVersion, string ModelVersion, string Trainer, string MlNetVersion,
    string DatasetVersion, DateTimeOffset TrainedAtUtc, string[] Features, string[] Labels, double ReferenceThreshold,
    bool UseClassWeights, float L2Regularization, Dictionary<string, float> ClassWeights,
    string ModelSha256, Dictionary<string, string> DatasetSha256);
