using System.Text.Json;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record CategoryTrainingReport(string ModelVersion, string Trainer, int Seed, int Iterations, int Threads,
    float L2Regularization, string SelectionPolicy, string MajorityLabel, CategoryEvaluation Validation, CategoryEvaluation Test);

public static class CategoryTrainer
{
    public static CategoryTrainingReport Train(string datasetDirectory, string outputDirectory, string modelVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        if (modelVersion.Length > 100) throw new ArgumentException("Model version must be at most 100 characters.", nameof(modelVersion));
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Model output already exists; use a new version directory.");
        var dataset = TicketDataset.Load(datasetDirectory);
        var ml = new MLContext(seed: CategoryTrainingPipeline.Seed);
        var trainingData = ml.Data.LoadFromEnumerable(dataset.Training.Select(row => row.ToModelInput()));
        // All learned transforms and trainer parameters are fitted exclusively on training.
        var model = CategoryTrainingPipeline.Create(ml).Fit(trainingData);
        var labels = CategoryModelStore.GetScoreLabels(model, trainingData.Schema);
        var majorityLabel = dataset.Training.GroupBy(row => row.Category).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;
        var validation = CategoryEvaluation.Evaluate(ml, model, dataset.Validation, labels, majorityLabel);
        validation.Print("validation");
        // Predeclared single baseline pipeline: no test-driven model/hyperparameter search.
        var test = CategoryEvaluation.Evaluate(ml, model, dataset.Test, labels, majorityLabel);
        test.Print("test");
        var report = new CategoryTrainingReport(modelVersion, "SdcaMaximumEntropy", CategoryTrainingPipeline.Seed,
            CategoryTrainingPipeline.Iterations, 1, 0.01f, "One predefined pipeline; fit training only; validation reported before one final test evaluation.", majorityLabel, validation, test);
        var metadata = new CategoryModelMetadata(1, modelVersion, "SdcaMaximumEntropy", "5.0.1", dataset.Version,
            DateTimeOffset.UtcNow, ["Title", "Description"], labels, "", dataset.Hashes);
        CategoryModelStore.Save(outputDirectory, ml, model, trainingData.Schema, metadata, JsonSerializer.Serialize(report, CategoryModelStore.JsonOptions));
        Console.WriteLine("Saved model: " + Path.GetFullPath(outputDirectory));
        return report;
    }
}
