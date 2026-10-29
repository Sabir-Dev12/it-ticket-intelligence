using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;

namespace ITTicketIntelligence.Training;

public static class PriorityTrainingPipeline
{
    public const int Seed = 20261024;
    public const int Iterations = 200;

    public static IEstimator<ITransformer> Create(MLContext ml, float l2, bool useClassWeights) =>
        ml.Transforms.Conversion.MapValueToKey("Label", nameof(PriorityModelInput.Priority), keyOrdinality: ValueToKeyMappingEstimator.KeyOrdinality.ByValue)
            .Append(ml.Transforms.Text.FeaturizeText("TextFeatures", new TextFeaturizingEstimator.Options
            {
                CaseMode = TextNormalizingEstimator.CaseMode.Lower, KeepDiacritics = true,
                KeepNumbers = true, KeepPunctuations = false, Norm = TextFeaturizingEstimator.NormFunction.L2
            }, nameof(PriorityModelInput.Title), nameof(PriorityModelInput.Description)))
            .Append(ml.Transforms.Categorical.OneHotEncoding("CategoryFeatures", nameof(PriorityModelInput.Category)))
            .Append(ml.Transforms.Concatenate("Features", "TextFeatures", "CategoryFeatures"))
            .Append(ml.Transforms.NormalizeLpNorm("Features"))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "Label", FeatureColumnName = "Features", MaximumNumberOfIterations = Iterations,
                NumberOfThreads = 1, L2Regularization = l2,
                ExampleWeightColumnName = useClassWeights ? nameof(PriorityModelInput.ExampleWeight) : null
            }))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedPriority", "PredictedLabel"));

    public static PriorityModelInput ToInput(DatasetTicket row, float weight = 1) => new()
    {
        Title = row.Title, Description = row.Description, Category = row.Category,
        Priority = row.Priority, ExampleWeight = weight
    };
}
