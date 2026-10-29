using System.Text.Json;
using System.Text.Json.Nodes;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using ITTicketIntelligence.Training;
using Microsoft.ML;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

public sealed class PriorityModelTests
{
    private static string Root => CategoryModelTests.Root;
    private static string ModelDirectory => Path.Combine(Root, "models", "ticket-priority", "v1");
    private static TicketPriorityInput Input => new("Production service unavailable to every office", "All employees cannot reach the transaction service. Both connections failed and there is no alternative access.", TicketCategory.Network);

    [Fact]
    public async Task LoadedModelMapsTheCorrectScoreSlotAndDoesNotApplyAnotherSoftmax()
    {
        var loaded = PriorityModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<PriorityModelInput, PriorityModelOutput>(loaded.Model);
        var raw = engine.Predict(new PriorityModelInput { Title = Input.Title, Description = Input.Description, Category = Input.Category.ToString() });
        using var predictor = new TicketPriorityPredictor(ModelDirectory);
        var result = await predictor.PredictAsync(Input);
        var index = Array.IndexOf(raw.Score, raw.Score.Max());
        Assert.Equal(loaded.Metadata.Labels[index], result.PredictedPriority.ToString());
        Assert.Equal(raw.PredictedPriority, result.PredictedPriority.ToString());
        Assert.Equal(raw.Score[index], result.Confidence, 7);
        Assert.InRange(result.Confidence, 0, 1);
        Assert.Equal(loaded.Metadata.ModelVersion, result.ModelVersion);
        Assert.Equal(new[] { "Title", "Description", "Category" }, loaded.Metadata.Features);
        Assert.Equal(PriorityModelStore.CategoryInputPolicy, loaded.Metadata.CategoryInputPolicy);
    }

    [Fact]
    public void TrainingTargetAndExampleWeightAreNotPredictionFeatures()
    {
        var loaded = PriorityModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<PriorityModelInput, PriorityModelOutput>(loaded.Model);
        var first = engine.Predict(new PriorityModelInput { Title = Input.Title, Description = Input.Description, Category = "Network", Priority = "Low", ExampleWeight = 1 });
        var second = engine.Predict(new PriorityModelInput { Title = Input.Title, Description = Input.Description, Category = "Network", Priority = "Critical", ExampleWeight = 100 });
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.PredictedPriority, second.PredictedPriority);
    }

    [Fact]
    public void SuppliedCategoryIsEncodedAsANominalFeature()
    {
        var loaded = PriorityModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<PriorityModelInput, CategoryFeatureProbe>(loaded.Model);
        var network = engine.Predict(new PriorityModelInput { Title = Input.Title, Description = Input.Description, Category = "Network" }).CategoryFeatures;
        var billing = engine.Predict(new PriorityModelInput { Title = Input.Title, Description = Input.Description, Category = "Billing" }).CategoryFeatures;
        Assert.Equal(9, network.Length);
        Assert.Equal(1f, network.Sum());
        Assert.Equal(1f, billing.Sum());
        Assert.NotEqual(Array.IndexOf(network, 1f), Array.IndexOf(billing, 1f));
    }

    [Fact]
    public async Task SharedPredictorSupportsConcurrentCallsAndCancellation()
    {
        using var predictor = new TicketPriorityPredictor(ModelDirectory);
        var expected = await predictor.PredictAsync(Input);
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => predictor.PredictAsync(Input))));
        Assert.All(results, result => Assert.Equal(expected, result));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => predictor.PredictAsync(Input, cancellation.Token));
    }

    [Fact]
    public async Task PredictionKeepsTheLoadedModelInMemory()
    {
        using var temporary = new TemporaryModel();
        using var predictor = new TicketPriorityPredictor(temporary.Path);
        var first = await predictor.PredictAsync(Input);
        File.Delete(Path.Combine(temporary.Path, "model.zip"));
        File.Delete(Path.Combine(temporary.Path, "metadata.json"));
        Assert.Equal(first, await predictor.PredictAsync(Input));
    }

    [Fact]
    public void MissingAndCorruptedModelsAreRejected()
    {
        using var temporary = new TemporaryModel();
        var path = Path.Combine(temporary.Path, "model.zip");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => new TicketPriorityPredictor(temporary.Path));
        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => new TicketPriorityPredictor(temporary.Path));
    }

    [Theory]
    [InlineData("features")]
    [InlineData("labels")]
    [InlineData("order")]
    [InlineData("policy")]
    [InlineData("version")]
    public void IncompatibleMetadataIsRejected(string defect)
    {
        using var temporary = new TemporaryModel();
        var path = Path.Combine(temporary.Path, "metadata.json");
        var metadata = JsonNode.Parse(File.ReadAllText(path))!;
        if (defect == "features") metadata["features"] = new JsonArray("Title", "Description", "Priority");
        else if (defect == "labels") metadata["labels"]![0] = "Urgent";
        else if (defect == "order") metadata["labels"] = new JsonArray(metadata["labels"]!.AsArray().Reverse().Select(node => node!.DeepClone()).ToArray());
        else if (defect == "policy") metadata["categoryInputPolicy"] = "post-resolution-category";
        else metadata["contractVersion"] = 2;
        File.WriteAllText(path, metadata.ToJsonString());
        Assert.Throws<InvalidDataException>(() => new TicketPriorityPredictor(temporary.Path));
    }

    [Fact]
    public void ClassWeightsAreDerivedFromTrainingCountsOnly()
    {
        var rows = new[] { ("Low", 3), ("Medium", 6), ("High", 5), ("Critical", 2) }
            .SelectMany(item => Enumerable.Range(0, item.Item2).Select(_ => new DatasetTicket(Guid.NewGuid(), Guid.NewGuid(), "Title", "Description", "Network") { Priority = item.Item1 })).ToArray();
        var balanced = PriorityTrainer.ComputeClassWeights(rows, balanced: true);
        Assert.Equal(4, balanced.Count);
        Assert.True(balanced["Critical"] > balanced["Medium"]);
        Assert.Equal(rows.Length, rows.Sum(row => (double)balanced[row.Priority]), 5);
        Assert.All(PriorityTrainer.ComputeClassWeights(rows, balanced: false).Values, weight => Assert.Equal(1f, weight));
        Assert.Throws<InvalidDataException>(() => PriorityTrainer.ComputeClassWeights(rows.Where(row => row.Priority != "Critical").ToArray(), true));
        Assert.Throws<InvalidDataException>(() => PriorityTrainer.ComputeClassWeights([], true));
    }

    [Fact]
    public void CandidateSelectionUsesValidationMetricsAndDeterministicTieBreaks()
    {
        var report = JsonSerializer.Deserialize<PriorityTrainingReport>(File.ReadAllText(Path.Combine(ModelDirectory, "evaluation.json")), PriorityModelStore.JsonOptions)!;
        var baseline = report.Candidates[0];
        var candidates = new[]
        {
            baseline with { Id = "f1", Validation = baseline.Validation with { MacroF1 = 0.8, CriticalRecall = 0, LogLoss = 1 } },
            baseline with { Id = "recall", Validation = baseline.Validation with { MacroF1 = 0.7, CriticalRecall = 1, LogLoss = 0.1 } },
        };
        Assert.Equal("f1", PriorityTrainer.SelectCandidate(candidates).Id);
        var highRecall = candidates[0] with { Id = "high-recall", Validation = candidates[0].Validation with { CriticalRecall = 0.5 } };
        Assert.Equal("high-recall", PriorityTrainer.SelectCandidate([candidates[0], highRecall]).Id);
        var lowLoss = highRecall with { Id = "lower-loss", Validation = highRecall.Validation with { LogLoss = 0.2 } };
        Assert.Equal("lower-loss", PriorityTrainer.SelectCandidate([highRecall, lowLoss]).Id);
        Assert.Equal("a", PriorityTrainer.SelectCandidate([lowLoss with { Id = "z" }, lowLoss with { Id = "a" }]).Id);
    }

    [Fact]
    public async Task TrainEvaluateSaveAndLoadRoundTripPreservesThePriorityContract()
    {
        var directory = Path.Combine(Path.GetTempPath(), "itt-priority-training-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = PriorityTrainer.Train(Path.Combine(Root, "data", "tickets", "v1"), directory, "priority-roundtrip");
            Assert.Equal(6, report.Candidates.Length);
            Assert.Equal(PriorityTrainer.SelectCandidate(report.Candidates).Id, report.SelectedCandidate);
            foreach (var evaluation in new[] { report.Validation, report.Test })
            {
                Assert.Equal(36, evaluation.Tickets);
                Assert.Equal(18, evaluation.Scenarios);
                Assert.Equal(new[] { "Low", "Medium", "High", "Critical" }, evaluation.Labels);
                Assert.Equal(evaluation.Tickets, evaluation.ConfusionMatrix.Sum(row => row.Sum()));
                Assert.Equal(evaluation.ConfusionMatrix.Select((row, index) => row[index]).Sum() / evaluation.Tickets, evaluation.Accuracy, 7);
                Assert.Equal(evaluation.PerClass.Average(item => item.Recall), evaluation.MacroAccuracy, 7);
                Assert.Equal(evaluation.PerClass[3].Recall, evaluation.CriticalRecall);
                Assert.Equal(evaluation.PerClass[3].Support - (int)evaluation.ConfusionMatrix[3][3], evaluation.CriticalFalseNegatives);
                Assert.InRange(evaluation.MacroF1, 0, 1);
                Assert.True(double.IsFinite(evaluation.LogLoss) && evaluation.LogLoss >= 0);
                Assert.InRange(evaluation.MeanAbsoluteOrdinalError, 0, 3);
                Assert.Equal(4, evaluation.PerClass[3].Support);
                Assert.Equal(2, evaluation.PerClass[3].Scenarios);
            }
            using var predictor = new TicketPriorityPredictor(directory);
            Assert.Equal("priority-roundtrip", (await predictor.PredictAsync(Input)).ModelVersion);
            Assert.Throws<IOException>(() => PriorityTrainer.Train(Path.Combine(Root, "data", "tickets", "v1"), directory, "no-overwrite"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOrConflictingPriorityLabelsAreRejectedBeforeTraining(bool conflicting)
    {
        var directory = Path.Combine(Path.GetTempPath(), "itt-priority-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in new[] { "manifest.json", "training.csv", "validation.csv", "test.csv" })
                File.Copy(Path.Combine(Root, "data", "tickets", "v1", file), Path.Combine(directory, file));
            var path = Path.Combine(directory, "training.csv");
            var lines = File.ReadAllLines(path);
            // Last fields contain only taxonomy labels, so the suffix can be changed without reparsing quoted descriptions.
            var lastComma = lines[1].LastIndexOf(',');
            var previousComma = lines[1].LastIndexOf(',', lastComma - 1);
            var current = lines[1][(previousComma + 1)..lastComma];
            var replacement = conflicting ? (current == "Low" ? "Medium" : "Low") : "Urgent";
            lines[1] = lines[1][..(previousComma + 1)] + replacement + lines[1][lastComma..];
            File.WriteAllLines(path, lines);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["artifacts_sha256"]!["training.csv"] = PriorityModelStore.HashFile(path);
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            Assert.Throws<InvalidDataException>(() => PriorityTrainer.Train(directory, Path.Combine(directory, "output"), "invalid"));
            Assert.False(Directory.Exists(Path.Combine(directory, "output")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CliRejectsAnOmittedCategoryRatherThanUsingTheEnumDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "itt-priority-input-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"title\":\"Title\",\"description\":\"Description\"}");
            Assert.Equal(1, await Training.Program.Main(["predict-priority", "--model", ModelDirectory, "--input", path]));
        }
        finally { File.Delete(path); }
    }

    private sealed class CategoryFeatureProbe
    {
        public float[] CategoryFeatures { get; set; } = [];
    }

    private sealed class TemporaryModel : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-priority-model-" + Guid.NewGuid().ToString("N"));
        public TemporaryModel()
        {
            Directory.CreateDirectory(Path);
            foreach (var file in new[] { "model.zip", "metadata.json" }) File.Copy(System.IO.Path.Combine(ModelDirectory, file), System.IO.Path.Combine(Path, file));
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
