using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Infrastructure.MachineLearning;

namespace ITTicketIntelligence.Training;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is not ("train" or "predict" or "train-priority" or "predict-priority" or "train-team" or "predict-team"))
                throw new ArgumentException("Use train/train-priority/train-team --data <dataset> --output <new-model-directory> --version <version> OR predict/predict-priority/predict-team --model <directory> --input <json-file> [--threshold <team-only>]");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Invalid or duplicate command option.");
            string Option(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Missing option {key}.");
            var allowed = args[0] is "train" or "train-priority" or "train-team" ? new[] { "--data", "--output", "--version" }
                : args[0] == "predict-team" ? new[] { "--model", "--input", "--threshold" } : new[] { "--model", "--input" };
            if (options.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown command option.");
            if (args[0] == "train")
                CategoryTrainer.Train(Option("--data"), Option("--output"), Option("--version"));
            else if (args[0] == "train-priority")
                PriorityTrainer.Train(Option("--data"), Option("--output"), Option("--version"));
            else if (args[0] == "train-team")
                TeamTrainingService.Train(Option("--data"), Option("--output"), Option("--version"));
            else if (args[0] == "predict-team")
            {
                var json = new JsonSerializerOptions(TeamModelStore.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                var fields = JsonSerializer.Deserialize<TeamCommandInput>(File.ReadAllText(Option("--input")), json) ?? throw new InvalidDataException("Team input is empty.");
                var threshold = options.TryGetValue("--threshold", out var value) ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : TeamRecommendationPolicy.DefaultThreshold;
                using var predictor = new TicketTeamPredictor(Option("--model"), new TeamRecommendationPolicy(threshold));
                Console.WriteLine(JsonSerializer.Serialize(await predictor.PredictAsync(new TicketTeamInput(fields.Title, fields.Description)), json));
            }
            else if (args[0] == "predict-priority")
            {
                var json = new JsonSerializerOptions(PriorityModelStore.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                json.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
                var fields = JsonSerializer.Deserialize<PriorityCommandInput>(File.ReadAllText(Option("--input")), json)
                    ?? throw new InvalidDataException("Priority input document is empty.");
                var input = new TicketPriorityInput(fields.Title, fields.Description, fields.Category);
                using var predictor = new TicketPriorityPredictor(Option("--model"));
                Console.WriteLine(JsonSerializer.Serialize(await predictor.PredictAsync(input), json));
            }
            else
            {
                var input = JsonSerializer.Deserialize<TicketCategoryInput>(File.ReadAllText(Option("--input")), CategoryModelStore.JsonOptions)
                    ?? throw new InvalidDataException("Input document is empty.");
                using var predictor = new TicketCategoryPredictor(Option("--model"));
                var result = await predictor.PredictAsync(input);
                var json = new JsonSerializerOptions(CategoryModelStore.JsonOptions);
                json.Converters.Add(new JsonStringEnumConverter());
                Console.WriteLine(JsonSerializer.Serialize(result, json));
            }
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine("ML training tool failed: " + exception.Message);
            return 1;
        }
    }

    private sealed record PriorityCommandInput
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required ITTicketIntelligence.Domain.Enums.TicketCategory Category { get; init; }
    }

    private sealed record TeamCommandInput
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
    }
}
