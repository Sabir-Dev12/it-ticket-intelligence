using System.Text.Json;
using System.Text.Json.Nodes;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using ITTicketIntelligence.Training;
using Microsoft.ML;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

public sealed class CategoryModelTests
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "data", "tickets", "v1"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Run category tests from the project checkout.");
        }
    }

    internal static string ModelDirectory => Path.Combine(Root, "models", "ticket-category", "v1");
    private static TicketCategoryInput Invoice => new("Cannot download my invoice PDF", "The invoice opens online, but its downloaded PDF is empty. Other pages work normally.");

    [Fact]
    public async Task SavedModelReturnsTheCorrectScoreSlotWithoutApplyingSoftmaxAgain()
    {
        var loaded = CategoryModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<CategoryModelInput, CategoryModelOutput>(loaded.Model);
        var raw = engine.Predict(new CategoryModelInput { Title = Invoice.Title, Description = Invoice.Description });
        using var predictor = new TicketCategoryPredictor(ModelDirectory);
        var result = await predictor.PredictAsync(Invoice);
        var index = Array.IndexOf(raw.Score, raw.Score.Max());
        Assert.Equal(loaded.Metadata.Labels[index], result.Category.ToString());
        Assert.Equal(raw.PredictedCategory, result.Category.ToString());
        Assert.Equal(raw.Score[index], result.Confidence, 7);
        Assert.InRange(result.Confidence, 0, 1);
        Assert.Equal(loaded.Metadata.ModelVersion, result.ModelVersion);
    }

    [Fact]
    public void TrueCategoryIsNotAFeatureAtInference()
    {
        var loaded = CategoryModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<CategoryModelInput, CategoryModelOutput>(loaded.Model);
        var first = engine.Predict(new CategoryModelInput { Title = Invoice.Title, Description = Invoice.Description, Category = "Billing" });
        var second = engine.Predict(new CategoryModelInput { Title = Invoice.Title, Description = Invoice.Description, Category = "Security" });
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.PredictedCategory, second.PredictedCategory);
    }

    [Fact]
    public async Task ConcurrentPredictionsAreStableAndCancellationIsHonored()
    {
        using var predictor = new TicketCategoryPredictor(ModelDirectory);
        var expected = await predictor.PredictAsync(Invoice);
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => predictor.PredictAsync(Invoice))));
        Assert.All(results, result => Assert.Equal(expected, result));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => predictor.PredictAsync(Invoice, cancellation.Token));
    }

    [Fact]
    public async Task ModelFilesAreNotReadForEachPrediction()
    {
        using var temporary = new TemporaryModel();
        using var predictor = new TicketCategoryPredictor(temporary.Path);
        var expected = await predictor.PredictAsync(Invoice);
        File.Delete(System.IO.Path.Combine(temporary.Path, CategoryModelStore.ModelFileName));
        File.Delete(System.IO.Path.Combine(temporary.Path, CategoryModelStore.MetadataFileName));
        Assert.Equal(expected, await predictor.PredictAsync(Invoice));
    }

    [Fact]
    public void MissingOrCorruptedModelIsRejectedBeforeServingRequests()
    {
        using var temporary = new TemporaryModel();
        var model = System.IO.Path.Combine(temporary.Path, CategoryModelStore.ModelFileName);
        var bytes = File.ReadAllBytes(model);
        bytes[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(model, bytes);
        Assert.Throws<InvalidDataException>(() => new TicketCategoryPredictor(temporary.Path));
        File.Delete(model);
        Assert.Throws<InvalidDataException>(() => new TicketCategoryPredictor(temporary.Path));
    }

    [Theory]
    [InlineData("features")]
    [InlineData("labels")]
    [InlineData("order")]
    [InlineData("version")]
    public void IncompatibleMetadataAndWrongScoreOrderAreRejected(string defect)
    {
        using var temporary = new TemporaryModel();
        var path = System.IO.Path.Combine(temporary.Path, CategoryModelStore.MetadataFileName);
        var metadata = JsonNode.Parse(File.ReadAllText(path))!;
        if (defect == "features") metadata["features"] = new JsonArray("Title", "Category");
        else if (defect == "labels") metadata["labels"]![0] = "Unknown";
        else if (defect == "order") metadata["labels"] = new JsonArray(metadata["labels"]!.AsArray().Reverse().Select(node => node!.DeepClone()).ToArray());
        else metadata["contractVersion"] = 99;
        File.WriteAllText(path, metadata.ToJsonString());
        Assert.Throws<InvalidDataException>(() => new TicketCategoryPredictor(temporary.Path));
    }

    [Fact]
    public async Task TrainingEvaluationPersistenceAndReloadFormACompleteRoundTrip()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-category-training-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = CategoryTrainer.Train(System.IO.Path.Combine(Root, "data", "tickets", "v1"), directory, "round-trip-test");
            foreach (var evaluation in new[] { report.Validation, report.Test })
            {
                Assert.Equal(36, evaluation.Tickets);
                Assert.Equal(18, evaluation.Scenarios);
                Assert.Equal(9, evaluation.Labels.Length);
                Assert.InRange(evaluation.Accuracy, 0, 1);
                Assert.InRange(evaluation.MacroAccuracy, 0, 1);
                Assert.True(double.IsFinite(evaluation.LogLoss) && evaluation.LogLoss >= 0);
                Assert.Equal(evaluation.Tickets, evaluation.ConfusionMatrix.Sum(row => row.Sum()));
                var correct = evaluation.ConfusionMatrix.Select((row, index) => row[index]).Sum();
                Assert.Equal(correct / evaluation.Tickets, evaluation.Accuracy, 7);
                Assert.Equal(evaluation.PerClass.Average(item => item.Recall), evaluation.MacroAccuracy, 7);
                Assert.Equal(evaluation.Tickets, evaluation.PerClass.Sum(item => item.Support));
            }
            using var predictor = new TicketCategoryPredictor(directory);
            var prediction = await predictor.PredictAsync(Invoice);
            Assert.Equal("round-trip-test", prediction.ModelVersion);
            Assert.True(Enum.IsDefined(prediction.Category));
            Assert.True(File.Exists(System.IO.Path.Combine(directory, "evaluation.json")));
            Assert.Throws<IOException>(() => CategoryTrainer.Train(System.IO.Path.Combine(Root, "data", "tickets", "v1"), directory, "do-not-overwrite"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DatasetChecksumDetectsChangesBeforeTraining()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-category-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in new[] { "manifest.json", "training.csv", "validation.csv", "test.csv" })
                File.Copy(System.IO.Path.Combine(Root, "data", "tickets", "v1", file), System.IO.Path.Combine(directory, file));
            File.AppendAllText(System.IO.Path.Combine(directory, "training.csv"), "tampered");
            Assert.Throws<InvalidDataException>(() => TicketDataset.Load(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DatasetScenarioLeakageIsDetectedEvenIfTheChecksumIsUpdated()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-category-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in new[] { "manifest.json", "training.csv", "validation.csv", "test.csv" })
                File.Copy(System.IO.Path.Combine(Root, "data", "tickets", "v1", file), System.IO.Path.Combine(directory, file));
            var trainingScenario = File.ReadLines(System.IO.Path.Combine(directory, "training.csv")).Skip(1).First().Split(',')[1];
            var path = System.IO.Path.Combine(directory, "validation.csv");
            var lines = File.ReadAllLines(path);
            lines[1] = lines[1].Replace(lines[1].Split(',')[1], trainingScenario, StringComparison.Ordinal);
            File.WriteAllLines(path, lines);
            var manifestPath = System.IO.Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["artifacts_sha256"]!["validation.csv"] = CategoryModelStore.HashFile(path);
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            var exception = Assert.Throws<InvalidDataException>(() => TicketDataset.Load(directory));
            Assert.Contains("scenario", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class TemporaryModel : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-category-model-" + Guid.NewGuid().ToString("N"));
        public TemporaryModel()
        {
            Directory.CreateDirectory(Path);
            foreach (var file in new[] { CategoryModelStore.ModelFileName, CategoryModelStore.MetadataFileName })
                File.Copy(System.IO.Path.Combine(ModelDirectory, file), System.IO.Path.Combine(Path, file));
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
