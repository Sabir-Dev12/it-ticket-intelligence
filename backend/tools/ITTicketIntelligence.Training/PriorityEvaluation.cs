using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.ML;

namespace ITTicketIntelligence.Training;

public sealed record PriorityClassMetrics(string Priority, int Support, int Scenarios, double Precision, double Recall, double F1);

public sealed record PriorityEvaluation(int Tickets, int Scenarios, double Accuracy, double MacroAccuracy, double LogLoss,
    double MacroF1, double MajorityBaselineAccuracy, double CriticalRecall, int CriticalFalseNegatives,
    int SevereUnderestimates, double MeanAbsoluteOrdinalError, string[] Labels, double[][] ConfusionMatrix, PriorityClassMetrics[] PerClass)
{
    // This business order deliberately differs from the model's alphabetical score-slot order.
    public static readonly string[] BusinessOrder = ["Low", "Medium", "High", "Critical"];

    public static PriorityEvaluation Evaluate(MLContext ml, ITransformer model, IReadOnlyList<DatasetTicket> rows, string[] scoreLabels, string majorityLabel)
    {
        var data = ml.Data.LoadFromEnumerable(rows.Select(row => PriorityTrainingPipeline.ToInput(row)));
        // Evaluation is unweighted even when training used class weights.
        var metrics = ml.MulticlassClassification.Evaluate(model.Transform(data), "Label", "Score", "PredictedLabel");
        var raw = metrics.ConfusionMatrix.Counts;
        if (scoreLabels.Length != 4 || raw.Count != 4 || !scoreLabels.Order().SequenceEqual(BusinessOrder.Order()))
            throw new InvalidDataException("Unexpected priority labels in evaluation.");
        var positions = BusinessOrder.Select(label => Array.IndexOf(scoreLabels, label)).ToArray();
        var matrix = positions.Select(actual => positions.Select(predicted => raw[actual][predicted]).ToArray()).ToArray();
        if (matrix.Sum(row => row.Sum()) != rows.Count || !double.IsFinite(metrics.LogLoss)) throw new InvalidDataException("Invalid priority evaluation output.");
        var classes = BusinessOrder.Select((label, index) =>
        {
            var actual = matrix[index].Sum();
            var predicted = matrix.Sum(row => row[index]);
            var tp = matrix[index][index];
            var precision = predicted == 0 ? 0 : tp / predicted;
            var recall = actual == 0 ? 0 : tp / actual;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            return new PriorityClassMetrics(label, (int)actual, rows.Where(row => row.Priority == label).Select(row => row.ScenarioId).Distinct().Count(), precision, recall, f1);
        }).ToArray();
        double ordinalError = 0;
        var severe = 0;
        for (var actual = 0; actual < 4; actual++)
            for (var predicted = 0; predicted < 4; predicted++)
            {
                ordinalError += matrix[actual][predicted] * Math.Abs(actual - predicted);
                if (actual - predicted >= 2) severe += (int)matrix[actual][predicted];
            }
        return new PriorityEvaluation(rows.Count, rows.Select(row => row.ScenarioId).Distinct().Count(), metrics.MicroAccuracy,
            metrics.MacroAccuracy, metrics.LogLoss, classes.Average(item => item.F1), (double)rows.Count(row => row.Priority == majorityLabel) / rows.Count,
            classes[3].Recall, classes[3].Support - (int)matrix[3][3], severe, ordinalError / rows.Count,
            BusinessOrder.ToArray(), matrix, classes);
    }

    public void Print(string split)
    {
        Console.WriteLine($"{split}: accuracy={Accuracy:F6}, macro accuracy={MacroAccuracy:F6}, log loss={LogLoss:F6}, macro F1={MacroF1:F6}, Critical recall={CriticalRecall:F6}, Critical FN={CriticalFalseNegatives}");
        Console.WriteLine("Confusion matrix: rows=actual, columns=predicted; order=Low, Medium, High, Critical");
        foreach (var row in ConfusionMatrix) Console.WriteLine(string.Join(" ", row.Select(value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
    }
}
