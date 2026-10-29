using ITTicketIntelligence.Application.Intelligence;
using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record TeamClassMetrics(string Team, int Support, int Scenarios, double Precision, double Recall, double F1);
public sealed record TeamReviewMetrics(double Threshold, int Accepted, int NeedsHumanReview, int AcceptedCorrect,
    int AcceptedErrors, double Coverage, double? AccuracyWhenAccepted);
public sealed record TeamEvaluation(int Tickets, int Scenarios, double Accuracy, double MacroAccuracy, double LogLoss,
    double MacroF1, double Top2Accuracy, double MajorityBaselineAccuracy, string[] Labels, double[][] ConfusionMatrix,
    TeamClassMetrics[] PerClass, TeamReviewMetrics Review)
{
    public void Print(string split)
    {
        Console.WriteLine($"{split}: accuracy={Accuracy:F6}, macro accuracy={MacroAccuracy:F6}, macro F1={MacroF1:F6}, log loss={LogLoss:F6}, top2={Top2Accuracy:F6}");
        Console.WriteLine($"threshold={Review.Threshold:F2}, accepted={Review.Accepted}, review={Review.NeedsHumanReview}, coverage={Review.Coverage:F6}, accepted accuracy={Review.AccuracyWhenAccepted?.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}");
        Console.WriteLine("Confusion matrix: rows=actual, columns=predicted; order=" + string.Join(", ", Labels));
        foreach (var row in ConfusionMatrix) Console.WriteLine(string.Join(" ", row.Select(value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
    }
}

public sealed class TeamEvaluationRow
{
    public string RecommendedTeam { get; set; } = "";
    public string PredictedTeam { get; set; } = "";
    public float[] Score { get; set; } = [];
}

public static class TeamEvaluationService
{
    public static TeamEvaluation Evaluate(MLContext ml, ITransformer model, IReadOnlyList<DatasetTicket> rows,
        string[] scoreLabels, string majority, double threshold)
    {
        var data = ml.Data.LoadFromEnumerable(rows.Select(row => TeamTrainingPipeline.ToInput(row)));
        var scored = model.Transform(data);
        var metrics = ml.MulticlassClassification.Evaluate(scored, "Label", "Score", "PredictedLabel", topKPredictionCount: 2);
        var raw = metrics.ConfusionMatrix.Counts;
        var labels = TicketTeamNames.All.ToArray();
        if (scoreLabels.Length != labels.Length || raw.Count != labels.Length || !scoreLabels.Order().SequenceEqual(labels.Order())) throw new InvalidDataException("Unexpected team evaluation labels.");
        var positions = labels.Select(label => Array.IndexOf(scoreLabels, label)).ToArray();
        var matrix = positions.Select(actual => positions.Select(predicted => raw[actual][predicted]).ToArray()).ToArray();
        if (matrix.Sum(row => row.Sum()) != rows.Count || !double.IsFinite(metrics.LogLoss)) throw new InvalidDataException("Invalid team evaluation output.");
        var perClass = labels.Select((label, index) =>
        {
            var actual = matrix[index].Sum();
            var predicted = matrix.Sum(row => row[index]);
            var tp = matrix[index][index];
            var precision = predicted == 0 ? 0 : tp / predicted;
            var recall = actual == 0 ? 0 : tp / actual;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            return new TeamClassMetrics(label, (int)actual, rows.Where(row => row.RecommendedTeam == label).Select(row => row.ScenarioId).Distinct().Count(), precision, recall, f1);
        }).ToArray();
        var review = EvaluateReview(ml.Data.CreateEnumerable<TeamEvaluationRow>(scored, reuseRowObject: false), threshold);
        return new TeamEvaluation(rows.Count, rows.Select(row => row.ScenarioId).Distinct().Count(), metrics.MicroAccuracy,
            metrics.MacroAccuracy, metrics.LogLoss, perClass.Average(item => item.F1), metrics.TopKAccuracy,
            (double)rows.Count(row => row.RecommendedTeam == majority) / rows.Count, labels, matrix, perClass, review);
    }

    public static TeamReviewMetrics EvaluateReview(IEnumerable<TeamEvaluationRow> rows, double threshold)
    {
        var policy = new TeamRecommendationPolicy(threshold);
        var total = 0;
        var accepted = 0;
        var correct = 0;
        foreach (var row in rows)
        {
            if (row.Score.Length != TicketTeamNames.All.Count || row.Score.Any(score => !float.IsFinite(score) || score < 0 || score > 1) || Math.Abs(row.Score.Sum(value => (double)value) - 1) > 0.001)
                throw new InvalidDataException("Invalid team scores during review evaluation.");
            var result = policy.Apply(row.PredictedTeam, row.Score.Max(), "evaluation");
            total++;
            if (!result.NeedsHumanReview)
            {
                accepted++;
                if (row.PredictedTeam == row.RecommendedTeam) correct++;
            }
        }
        return new TeamReviewMetrics(threshold, accepted, total - accepted, correct, accepted - correct,
            total == 0 ? 0 : (double)accepted / total, accepted == 0 ? null : (double)correct / accepted);
    }
}
