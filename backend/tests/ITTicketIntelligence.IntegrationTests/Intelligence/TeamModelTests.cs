using System.Text.Json;
using System.Text.Json.Nodes;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using ITTicketIntelligence.Training;
using Microsoft.ML;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

public sealed class TeamModelTests
{
    private static string Root => CategoryModelTests.Root;
    private static string ModelDirectory => Path.Combine(Root, "models", "ticket-team", "v1");
    private static TicketTeamInput Input => new("SQL transaction lock waits", "Production order writes wait behind a long database transaction and the processing queue keeps growing.");

    [Fact]
    public async Task PredictorMapsScoreSlotsAndActuallyAbstains()
    {
        var loaded = TeamModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<TeamModelInput, TeamModelOutput>(loaded.Model);
        var raw = engine.Predict(new TeamModelInput { Title = Input.Title, Description = Input.Description });
        var index = Array.IndexOf(raw.Score, raw.Score.Max());
        Assert.Equal(raw.PredictedTeam, loaded.Metadata.Labels[index]);
        var policy = new TeamRecommendationPolicy();
        using var predictor = new TicketTeamPredictor(ModelDirectory, policy);
        var result = await predictor.PredictAsync(Input);
        Assert.Equal(policy.Apply(raw.PredictedTeam, raw.Score[index], loaded.Metadata.ModelVersion), result);
        Assert.Equal(raw.Score[index], result.Confidence, 7);
        Assert.Equal(result.Confidence < result.Threshold, result.NeedsHumanReview);
        if (result.NeedsHumanReview) Assert.Null(result.Team);
        else Assert.Contains(result.Team, TicketTeamNames.All);
    }

    [Fact]
    public void NeitherTargetNorWeightsArePredictionFeatures()
    {
        var loaded = TeamModelStore.Load(ModelDirectory);
        using var engine = loaded.Context.Model.CreatePredictionEngine<TeamModelInput, TeamModelOutput>(loaded.Model);
        var first = engine.Predict(new TeamModelInput { Title = Input.Title, Description = Input.Description, RecommendedTeam = "Database", ExampleWeight = 1 });
        var second = engine.Predict(new TeamModelInput { Title = Input.Title, Description = Input.Description, RecommendedTeam = "Support", ExampleWeight = 100 });
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.PredictedTeam, second.PredictedTeam);
        Assert.Equal(new[] { "Title", "Description" }, loaded.Metadata.Features);
        Assert.Null(typeof(TeamModelInput).GetProperty("Category"));
        Assert.Null(typeof(TeamModelInput).GetProperty("Priority"));
    }

    [Fact]
    public async Task StandaloneTeamBundleWorksWithoutOtherModelsOrSubsequentDiskReads()
    {
        using var temporary = new TemporaryModel();
        Assert.Equal(2, Directory.GetFiles(temporary.Path).Length);
        using var predictor = new TicketTeamPredictor(temporary.Path, new TeamRecommendationPolicy());
        var expected = await predictor.PredictAsync(Input);
        File.Delete(Path.Combine(temporary.Path, "model.zip"));
        File.Delete(Path.Combine(temporary.Path, "metadata.json"));
        Assert.Equal(expected, await predictor.PredictAsync(Input));
    }

    [Fact]
    public async Task ConcurrentCallsAndCancellationPreserveThePolicy()
    {
        using var predictor = new TicketTeamPredictor(ModelDirectory, new TeamRecommendationPolicy());
        var expected = await predictor.PredictAsync(Input);
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => predictor.PredictAsync(Input))));
        Assert.All(results, result => Assert.Equal(expected, result));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => predictor.PredictAsync(Input, cancellation.Token));
    }

    [Fact]
    public void CorruptAndMissingModelFilesAreRejected()
    {
        using var temporary = new TemporaryModel();
        var path = Path.Combine(temporary.Path, "model.zip");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => new TicketTeamPredictor(temporary.Path, new TeamRecommendationPolicy()));
        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => new TicketTeamPredictor(temporary.Path, new TeamRecommendationPolicy()));
    }

    [Theory]
    [InlineData("features")]
    [InlineData("labels")]
    [InlineData("order")]
    [InlineData("threshold")]
    [InlineData("version")]
    public void IncompatibleMetadataIsRejected(string defect)
    {
        using var temporary = new TemporaryModel();
        var path = Path.Combine(temporary.Path, "metadata.json");
        var metadata = JsonNode.Parse(File.ReadAllText(path))!;
        if (defect == "features") metadata["features"] = new JsonArray("Title", "Description", "Category");
        else if (defect == "labels") metadata["labels"]![0] = "Unknown";
        else if (defect == "order") metadata["labels"] = new JsonArray(metadata["labels"]!.AsArray().Reverse().Select(node => node!.DeepClone()).ToArray());
        else if (defect == "threshold") metadata["referenceThreshold"] = 2;
        else metadata["contractVersion"] = 2;
        File.WriteAllText(path, metadata.ToJsonString());
        Assert.Throws<InvalidDataException>(() => new TicketTeamPredictor(temporary.Path, new TeamRecommendationPolicy()));
    }

    [Fact]
    public void ReviewEvaluationHandlesAllRejectedAndMeasuresAcceptedErrors()
    {
        var rows = new[]
        {
            new TeamEvaluationRow { RecommendedTeam = "Support", PredictedTeam = "Support", Score = [0.9f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f] },
            new TeamEvaluationRow { RecommendedTeam = "Network", PredictedTeam = "Support", Score = [0.9f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f] }
        };
        var allRejected = TeamEvaluationService.EvaluateReview(rows, 1);
        Assert.Equal(0, allRejected.Accepted);
        Assert.Equal(2, allRejected.NeedsHumanReview);
        Assert.Null(allRejected.AccuracyWhenAccepted);
        Assert.Equal(0, allRejected.Coverage);
        var accepted = TeamEvaluationService.EvaluateReview(rows, 0.8);
        Assert.Equal(2, accepted.Accepted);
        Assert.Equal(1, accepted.AcceptedCorrect);
        Assert.Equal(1, accepted.AcceptedErrors);
        Assert.Equal(0.5, accepted.AccuracyWhenAccepted);
        Assert.Equal(1, accepted.Coverage);
        Assert.Throws<InvalidDataException>(() => TeamEvaluationService.EvaluateReview([new TeamEvaluationRow { PredictedTeam = "Support", Score = [float.NaN] }], 0.8));
    }

    [Fact]
    public void WeightsAndSelectionDoNotUseTestMetrics()
    {
        var dataset = TicketDataset.Load(Path.Combine(Root, "data", "tickets", "v1"));
        var weights = TeamTrainingService.ComputeClassWeights(dataset.Training, true);
        Assert.Equal(6, weights.Count);
        Assert.Equal(dataset.Training.Count, dataset.Training.Sum(row => (double)weights[row.RecommendedTeam]), 4);
        Assert.True(weights["Database"] > weights["Support"]);
        Assert.All(TeamTrainingService.ComputeClassWeights(dataset.Training, false).Values, weight => Assert.Equal(1f, weight));
        Assert.Throws<InvalidDataException>(() => TeamTrainingService.ComputeClassWeights([], true));
        Assert.Throws<InvalidDataException>(() => TeamTrainingService.ComputeClassWeights(dataset.Training.Where(row => row.RecommendedTeam != "Database").ToArray(), true));
        var report = JsonSerializer.Deserialize<TeamTrainingReport>(File.ReadAllText(Path.Combine(ModelDirectory, "evaluation.json")), TeamModelStore.JsonOptions)!;
        var baseline = report.Candidates[0];
        var betterF1 = baseline with { Id = "better-f1", Validation = baseline.Validation with { MacroF1 = 0.9, LogLoss = 2 } };
        var betterLoss = baseline with { Id = "better-loss", Validation = baseline.Validation with { MacroF1 = 0.8, LogLoss = 0.1 } };
        Assert.Equal("better-f1", TeamTrainingService.SelectCandidate([betterLoss, betterF1]).Id);
        var tie = betterF1 with { Id = "lower-loss", Validation = betterF1.Validation with { LogLoss = 0.1 } };
        Assert.Equal("lower-loss", TeamTrainingService.SelectCandidate([betterF1, tie]).Id);
        Assert.Equal("a", TeamTrainingService.SelectCandidate([tie with { Id = "z" }, tie with { Id = "a" }]).Id);
    }

    [Fact]
    public async Task TrainingEvaluationStorageAndPredictionRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "itt-team-training-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = TeamTrainingService.Train(Path.Combine(Root, "data", "tickets", "v1"), directory, "team-roundtrip");
            Assert.Equal(6, report.Candidates.Length);
            Assert.Equal(TeamTrainingService.SelectCandidate(report.Candidates).Id, report.SelectedCandidate);
            foreach (var evaluation in new[] { report.Validation, report.Test })
            {
                Assert.Equal(36, evaluation.Tickets);
                Assert.Equal(18, evaluation.Scenarios);
                Assert.Equal(TicketTeamNames.All, evaluation.Labels);
                Assert.Equal(36d, evaluation.ConfusionMatrix.Sum(row => row.Sum()));
                Assert.Equal(evaluation.ConfusionMatrix.Select((row, index) => row[index]).Sum() / 36, evaluation.Accuracy, 7);
                Assert.Equal(evaluation.PerClass.Average(item => item.Recall), evaluation.MacroAccuracy, 7);
                Assert.InRange(evaluation.Top2Accuracy, evaluation.Accuracy, 1);
                Assert.Equal(36, evaluation.Review.Accepted + evaluation.Review.NeedsHumanReview);
                Assert.Equal((double)evaluation.Review.Accepted / 36, evaluation.Review.Coverage);
                Assert.Equal(evaluation.Review.Accepted, evaluation.Review.AcceptedCorrect + evaluation.Review.AcceptedErrors);
                if (evaluation.Review.Accepted > 0) Assert.Equal((double)evaluation.Review.AcceptedCorrect / evaluation.Review.Accepted, evaluation.Review.AccuracyWhenAccepted);
                else Assert.Null(evaluation.Review.AccuracyWhenAccepted);
            }
            using var predictor = new TicketTeamPredictor(directory, new TeamRecommendationPolicy());
            Assert.Equal("team-roundtrip", (await predictor.PredictAsync(Input)).ModelVersion);
            Assert.Throws<IOException>(() => TeamTrainingService.Train(Path.Combine(Root, "data", "tickets", "v1"), directory, "no-overwrite"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void UnknownTeamLabelsFailBeforeTraining()
    {
        var directory = Path.Combine(Path.GetTempPath(), "itt-team-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in new[] { "manifest.json", "training.csv", "validation.csv", "test.csv" }) File.Copy(Path.Combine(Root, "data", "tickets", "v1", file), Path.Combine(directory, file));
            var path = Path.Combine(directory, "training.csv");
            var lines = File.ReadAllLines(path);
            lines[1] = lines[1][..(lines[1].LastIndexOf(',') + 1)] + "Unknown";
            File.WriteAllLines(path, lines);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["artifacts_sha256"]!["training.csv"] = TeamModelStore.HashFile(path);
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            Assert.Throws<InvalidDataException>(() => TeamTrainingService.Train(directory, Path.Combine(directory, "output"), "invalid"));
            Assert.False(Directory.Exists(Path.Combine(directory, "output")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class TemporaryModel : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itt-team-model-" + Guid.NewGuid().ToString("N"));
        public TemporaryModel()
        {
            Directory.CreateDirectory(Path);
            foreach (var file in new[] { "model.zip", "metadata.json" }) File.Copy(System.IO.Path.Combine(ModelDirectory, file), System.IO.Path.Combine(Path, file));
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
