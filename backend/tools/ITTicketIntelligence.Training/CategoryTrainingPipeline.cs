using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;
using Microsoft.ML.Trainers;

namespace ITTicketIntelligence.Training;

public static class CategoryTrainingPipeline
{
    public const int Seed = 20261024;
    public const int Iterations = 200;

    public static IEstimator<ITransformer> Create(MLContext ml) =>
        ml.Transforms.Conversion.MapValueToKey("Label", nameof(CategoryModelInput.Category), keyOrdinality: ValueToKeyMappingEstimator.KeyOrdinality.ByValue)
            .Append(ml.Transforms.Text.FeaturizeText("Features", new TextFeaturizingEstimator.Options
            {
                CaseMode = TextNormalizingEstimator.CaseMode.Lower,
                KeepDiacritics = true,
                KeepNumbers = true,
                KeepPunctuations = false,
                Norm = TextFeaturizingEstimator.NormFunction.L2
            }, nameof(CategoryModelInput.Title), nameof(CategoryModelInput.Description)))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "Label", FeatureColumnName = "Features",
                MaximumNumberOfIterations = Iterations, NumberOfThreads = 1, L2Regularization = 0.01f
            }))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedCategory", "PredictedLabel"));
}
