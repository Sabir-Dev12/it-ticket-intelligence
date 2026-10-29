using System.Text.Json;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record TeamCandidate(string Id, float L2Regularization, bool UseClassWeights,
    Dictionary<string, float> ClassWeights, TeamEvaluation Validation);
public sealed record TeamDistribution(int Tickets, int Scenarios, Dictionary<string, int> ClassCounts, Dictionary<string, int> ClassScenarioCounts);
public sealed record TeamTrainingReport(string ModelVersion, string Trainer, int Seed, int Iterations, int Threads,
    string SelectionPolicy, string ThresholdPolicy, string MajorityLabel, string SelectedCandidate,
    Dictionary<string, TeamDistribution> Distributions, TeamCandidate[] Candidates, TeamEvaluation Validation, TeamEvaluation Test);

public static class TeamTrainingService
{
    public static Dictionary<string, float> ComputeClassWeights(IReadOnlyList<DatasetTicket> training, bool balanced)
    {
        if (training.Count == 0 || training.Any(row => !TicketTeamNames.All.Contains(row.RecommendedTeam, StringComparer.Ordinal))) throw new InvalidDataException("Invalid team training labels.");
        var counts = training.GroupBy(row => row.RecommendedTeam).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (counts.Count != TicketTeamNames.All.Count) throw new InvalidDataException("Every team must be represented in training.");
        return TicketTeamNames.All.ToDictionary(label => label, label => balanced ? (float)training.Count / (TicketTeamNames.All.Count * counts[label]) : 1f, StringComparer.Ordinal);
    }

    public static TeamCandidate SelectCandidate(IEnumerable<TeamCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Validation.MacroF1)
        .ThenBy(candidate => candidate.Validation.LogLoss)
        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First();

    public static TeamTrainingReport Train(string datasetDirectory, string outputDirectory, string modelVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        if (modelVersion.Length > 100) throw new ArgumentException("Team model version is too long.", nameof(modelVersion));
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Team output version already exists.");
        var dataset = TicketDataset.Load(datasetDirectory);
        var partitions = new Dictionary<string, IReadOnlyList<DatasetTicket>> { ["training"] = dataset.Training, ["validation"] = dataset.Validation, ["test"] = dataset.Test };
        foreach (var (split, rows) in partitions)
            if (!rows.Select(row => row.RecommendedTeam).Distinct().Order().SequenceEqual(TicketTeamNames.All.Order()) ||
                rows.GroupBy(row => row.ScenarioId).Any(group => group.Select(row => row.RecommendedTeam).Distinct().Count() != 1))
                throw new InvalidDataException($"Missing, unknown or conflicting team labels in {split}.");
        var distributions = partitions.ToDictionary(pair => pair.Key, pair => new TeamDistribution(pair.Value.Count,
            pair.Value.Select(row => row.ScenarioId).Distinct().Count(),
            TicketTeamNames.All.ToDictionary(label => label, label => pair.Value.Count(row => row.RecommendedTeam == label)),
            TicketTeamNames.All.ToDictionary(label => label, label => pair.Value.Where(row => row.RecommendedTeam == label).Select(row => row.ScenarioId).Distinct().Count())));
        var majority = dataset.Training.GroupBy(row => row.RecommendedTeam).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;
        var fitted = new List<(TeamCandidate Candidate, MLContext Ml, ITransformer Model, DataViewSchema Schema, string[] Labels)>();
        foreach (var l2 in new[] { 0.00001f, 0.0001f, 0.001f })
            foreach (var balanced in new[] { false, true })
            {
                var weights = ComputeClassWeights(dataset.Training, balanced);
                var ml = new MLContext(seed: TeamTrainingPipeline.Seed);
                var training = ml.Data.LoadFromEnumerable(dataset.Training.Select(row => TeamTrainingPipeline.ToInput(row, weights[row.RecommendedTeam])));
                var model = TeamTrainingPipeline.Create(ml, l2, balanced).Fit(training);
                var labels = TeamModelStore.GetScoreLabels(model, training.Schema);
                var validation = TeamEvaluationService.Evaluate(ml, model, dataset.Validation, labels, majority, TeamRecommendationPolicy.DefaultThreshold);
                var id = $"l2-{l2.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)}-{(balanced ? "balanced" : "unweighted")}";
                var candidate = new TeamCandidate(id, l2, balanced, weights, validation);
                fitted.Add((candidate, ml, model, training.Schema, labels));
                Console.WriteLine($"validation {id}: macro F1={validation.MacroF1:F6}, accuracy={validation.Accuracy:F6}, log loss={validation.LogLoss:F6}, coverage={validation.Review.Coverage:F6}");
            }
        var selected = SelectCandidate(fitted.Select(item => item.Candidate));
        var winner = fitted.Single(item => item.Candidate.Id == selected.Id);
        Console.WriteLine("Selected using validation only: " + selected.Id);
        selected.Validation.Print("validation");
        var test = TeamEvaluationService.Evaluate(winner.Ml, winner.Model, dataset.Test, winner.Labels, majority, TeamRecommendationPolicy.DefaultThreshold);
        test.Print("test");
        var report = new TeamTrainingReport(modelVersion, "SdcaMaximumEntropy", TeamTrainingPipeline.Seed, TeamTrainingPipeline.Iterations, 1,
            "Max validation macro-F1, then min validation log loss, then candidate ID. Test scored only after selection.",
            "Fixed reference threshold 0.80 before training; not optimized using test. No automatic assignment at any confidence.",
            majority, selected.Id, distributions, fitted.Select(item => item.Candidate).ToArray(), selected.Validation, test);
        var metadata = new TeamModelMetadata(1, modelVersion, "SdcaMaximumEntropy", "5.0.1", dataset.Version, DateTimeOffset.UtcNow,
            ["Title", "Description"], winner.Labels, TeamRecommendationPolicy.DefaultThreshold, selected.UseClassWeights,
            selected.L2Regularization, selected.ClassWeights, "", dataset.Hashes);
        TeamModelStore.Save(outputDirectory, winner.Ml, winner.Model, winner.Schema, metadata, JsonSerializer.Serialize(report, TeamModelStore.JsonOptions));
        Console.WriteLine("Saved team model: " + Path.GetFullPath(outputDirectory));
        return report;
    }
}
