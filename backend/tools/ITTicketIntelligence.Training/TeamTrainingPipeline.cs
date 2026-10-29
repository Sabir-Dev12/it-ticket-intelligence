using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;

namespace ITTicketIntelligence.Training;

public static class TeamTrainingPipeline
{
    public const int Seed = 20261024;
    public const int Iterations = 200;

    public static IEstimator<ITransformer> Create(MLContext ml, float l2, bool balanced) =>
        ml.Transforms.Conversion.MapValueToKey("Label", nameof(TeamModelInput.RecommendedTeam), keyOrdinality: ValueToKeyMappingEstimator.KeyOrdinality.ByValue)
            .Append(ml.Transforms.Text.FeaturizeText("Features", new TextFeaturizingEstimator.Options
            {
                CaseMode = TextNormalizingEstimator.CaseMode.Lower, KeepDiacritics = true,
                KeepNumbers = true, KeepPunctuations = false, Norm = TextFeaturizingEstimator.NormFunction.L2
            }, nameof(TeamModelInput.Title), nameof(TeamModelInput.Description)))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "Label", FeatureColumnName = "Features", MaximumNumberOfIterations = Iterations,
                NumberOfThreads = 1, L2Regularization = l2,
                ExampleWeightColumnName = balanced ? nameof(TeamModelInput.ExampleWeight) : null
            }))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedTeam", "PredictedLabel"));

    public static TeamModelInput ToInput(DatasetTicket row, float weight = 1) => new()
    {
        Title = row.Title, Description = row.Description, RecommendedTeam = row.RecommendedTeam, ExampleWeight = weight
    };
}
