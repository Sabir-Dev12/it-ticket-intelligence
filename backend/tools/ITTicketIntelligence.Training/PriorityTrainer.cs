using System.Text.Json;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record PriorityCandidate(string Id, float L2Regularization, bool UseClassWeights,
    Dictionary<string, float> ClassWeights, PriorityEvaluation Validation);
public sealed record PriorityDistribution(int Tickets, int Scenarios, Dictionary<string, int> ClassCounts, Dictionary<string, int> ClassScenarioCounts);
public sealed record PriorityTrainingReport(string ModelVersion, string Trainer, int Seed, int Iterations, int Threads,
    string CategoryInputPolicy, string SelectionPolicy, string MajorityLabel, string SelectedCandidate,
    Dictionary<string, PriorityDistribution> Distributions, PriorityCandidate[] Candidates, PriorityEvaluation Validation, PriorityEvaluation Test);

public static class PriorityTrainer
{
    public static Dictionary<string, float> ComputeClassWeights(IReadOnlyList<DatasetTicket> training, bool balanced)
    {
        if (training.Count == 0 || training.Any(row => !Enum.GetNames<TicketPriority>().Contains(row.Priority, StringComparer.Ordinal)))
            throw new InvalidDataException("Invalid priority training labels.");
        var counts = training.GroupBy(row => row.Priority).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (counts.Count != 4) throw new InvalidDataException("Every priority class must be represented in training.");
        return PriorityEvaluation.BusinessOrder.ToDictionary(label => label, label => balanced ? (float)training.Count / (4 * counts[label]) : 1f, StringComparer.Ordinal);
    }

    public static PriorityCandidate SelectCandidate(IEnumerable<PriorityCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Validation.MacroF1)
        .ThenByDescending(candidate => candidate.Validation.CriticalRecall)
        .ThenBy(candidate => candidate.Validation.LogLoss)
        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First();

    public static PriorityTrainingReport Train(string datasetDirectory, string outputDirectory, string modelVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        if (modelVersion.Length > 100) throw new ArgumentException("Model version must be at most 100 characters.", nameof(modelVersion));
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Priority output version already exists.");
        var dataset = TicketDataset.Load(datasetDirectory);
        var partitions = new Dictionary<string, IReadOnlyList<DatasetTicket>> { ["training"] = dataset.Training, ["validation"] = dataset.Validation, ["test"] = dataset.Test };
        foreach (var (split, rows) in partitions)
        {
            if (!rows.Select(row => row.Priority).Distinct().Order().SequenceEqual(Enum.GetNames<TicketPriority>().Order()) ||
                rows.GroupBy(row => row.ScenarioId).Any(group => group.Select(row => row.Priority).Distinct().Count() != 1))
                throw new InvalidDataException($"Missing, unknown or inconsistent priority labels in {split}.");
        }
        var distributions = partitions.ToDictionary(pair => pair.Key, pair => new PriorityDistribution(pair.Value.Count,
            pair.Value.Select(row => row.ScenarioId).Distinct().Count(),
            PriorityEvaluation.BusinessOrder.ToDictionary(label => label, label => pair.Value.Count(row => row.Priority == label)),
            PriorityEvaluation.BusinessOrder.ToDictionary(label => label, label => pair.Value.Where(row => row.Priority == label).Select(row => row.ScenarioId).Distinct().Count())));
        var majority = dataset.Training.GroupBy(row => row.Priority).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;
        var fitted = new List<(PriorityCandidate Candidate, MLContext Ml, ITransformer Model, DataViewSchema Schema, string[] Labels)>();
        // Predeclared grid. No model is ever fitted on validation or test.
        foreach (var l2 in new[] { 0.00001f, 0.0001f, 0.001f })
            foreach (var balanced in new[] { false, true })
            {
                var weights = ComputeClassWeights(dataset.Training, balanced);
                var ml = new MLContext(seed: PriorityTrainingPipeline.Seed);
                var trainingData = ml.Data.LoadFromEnumerable(dataset.Training.Select(row => PriorityTrainingPipeline.ToInput(row, weights[row.Priority])));
                var model = PriorityTrainingPipeline.Create(ml, l2, balanced).Fit(trainingData);
                var labels = PriorityModelStore.GetScoreLabels(model, trainingData.Schema);
                var validation = PriorityEvaluation.Evaluate(ml, model, dataset.Validation, labels, majority);
                var id = $"l2-{l2.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)}-{(balanced ? "balanced" : "unweighted")}";
                var candidate = new PriorityCandidate(id, l2, balanced, weights, validation);
                fitted.Add((candidate, ml, model, trainingData.Schema, labels));
                Console.WriteLine($"validation {id}: macro F1={validation.MacroF1:F6}, macro accuracy={validation.MacroAccuracy:F6}, Critical recall={validation.CriticalRecall:F6}, log loss={validation.LogLoss:F6}");
            }
        var selected = SelectCandidate(fitted.Select(item => item.Candidate));
        var winner = fitted.Single(item => item.Candidate.Id == selected.Id);
        Console.WriteLine("Selected using validation only: " + selected.Id);
        selected.Validation.Print("validation");
        // First prediction/evaluation of test occurs after selection is complete.
        var test = PriorityEvaluation.Evaluate(winner.Ml, winner.Model, dataset.Test, winner.Labels, majority);
        test.Print("test");
        var report = new PriorityTrainingReport(modelVersion, "SdcaMaximumEntropy", PriorityTrainingPipeline.Seed,
            PriorityTrainingPipeline.Iterations, 1, PriorityModelStore.CategoryInputPolicy,
            "Max validation macro-F1; ties: Critical recall descending, log loss ascending, candidate ID. Test evaluated only after selection.",
            majority, selected.Id, distributions, fitted.Select(item => item.Candidate).ToArray(), selected.Validation, test);
        var metadata = new PriorityModelMetadata(1, modelVersion, "SdcaMaximumEntropy", "5.0.1", dataset.Version, DateTimeOffset.UtcNow,
            ["Title", "Description", "Category"], winner.Labels, PriorityModelStore.CategoryInputPolicy,
            selected.UseClassWeights, selected.L2Regularization, selected.ClassWeights, "", dataset.Hashes);
        PriorityModelStore.Save(outputDirectory, winner.Ml, winner.Model, winner.Schema, metadata, JsonSerializer.Serialize(report, PriorityModelStore.JsonOptions));
        Console.WriteLine("Saved priority model: " + Path.GetFullPath(outputDirectory));
        return report;
    }
}
