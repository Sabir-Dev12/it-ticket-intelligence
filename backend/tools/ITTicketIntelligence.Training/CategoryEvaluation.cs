using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record CategoryClassMetrics(string Category, int Support, double Precision, double Recall, double F1);
public sealed record CategoryEvaluation(int Tickets, int Scenarios, double Accuracy, double MacroAccuracy, double LogLoss,
    double MacroF1, double MajorityBaselineAccuracy, string[] Labels, double[][] ConfusionMatrix, CategoryClassMetrics[] PerClass)
{
    public static CategoryEvaluation Evaluate(MLContext ml, ITransformer model, IReadOnlyList<DatasetTicket> rows, string[] labels, string majorityLabel)
    {
        var data = ml.Data.LoadFromEnumerable(rows.Select(row => row.ToModelInput()));
        var metrics = ml.MulticlassClassification.Evaluate(model.Transform(data), "Label", "Score", "PredictedLabel");
        var matrix = metrics.ConfusionMatrix.Counts.Select(row => row.ToArray()).ToArray();
        if (matrix.Length != labels.Length || matrix.Any(row => row.Length != labels.Length) ||
            matrix.Sum(row => row.Sum()) != rows.Count || !double.IsFinite(metrics.LogLoss))
            throw new InvalidDataException("Invalid multiclass evaluation output.");
        var classes = labels.Select((label, index) =>
        {
            var actual = matrix[index].Sum();
            var predicted = matrix.Sum(row => row[index]);
            var tp = matrix[index][index];
            var precision = predicted == 0 ? 0 : tp / predicted;
            var recall = actual == 0 ? 0 : tp / actual;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            return new CategoryClassMetrics(label, (int)actual, precision, recall, f1);
        }).ToArray();
        return new CategoryEvaluation(rows.Count, rows.Select(row => row.ScenarioId).Distinct().Count(), metrics.MicroAccuracy,
            metrics.MacroAccuracy, metrics.LogLoss, classes.Average(item => item.F1), (double)rows.Count(row => row.Category == majorityLabel) / rows.Count,
            labels, matrix, classes);
    }

    public void Print(string split)
    {
        Console.WriteLine($"{split}: tickets={Tickets}, scenarios={Scenarios}, accuracy={Accuracy:F6}, macro accuracy={MacroAccuracy:F6}, log loss={LogLoss:F6}, macro F1={MacroF1:F6}, majority baseline={MajorityBaselineAccuracy:F6}");
        Console.WriteLine("Confusion matrix: rows=actual, columns=predicted; order=" + string.Join(", ", Labels));
        foreach (var row in ConfusionMatrix) Console.WriteLine(string.Join(" ", row.Select(value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
    }
}
