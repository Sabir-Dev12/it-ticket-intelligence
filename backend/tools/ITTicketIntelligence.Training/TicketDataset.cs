using System.Text;
using System.Text.Json;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using Microsoft.VisualBasic.FileIO;

namespace ITTicketIntelligence.Training;

public sealed record DatasetTicket(Guid Id, Guid ScenarioId, string Title, string Description, string Category)
{
    public string Priority { get; init; } = "";
    public string RecommendedTeam { get; init; } = "";
    public CategoryModelInput ToModelInput() => new() { Title = Title, Description = Description, Category = Category };
}

public sealed record TicketDataset(string Version, Dictionary<string, string> Hashes,
    IReadOnlyList<DatasetTicket> Training, IReadOnlyList<DatasetTicket> Validation, IReadOnlyList<DatasetTicket> Test)
{
    private static readonly string[] Columns = ["TicketId", "ScenarioId", "Title", "Description", "Category", "Priority", "RecommendedTeam"];

    public static TicketDataset Load(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;
        Require(root.GetProperty("columns").EnumerateArray().Select(value => value.GetString()).SequenceEqual(Columns), "Unexpected dataset columns.");
        Require(root.GetProperty("features").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "Title", "Description" }), "Unexpected feature contract.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var splits = new List<DatasetTicket[]>();
        var ids = new HashSet<Guid>();
        var text = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<Guid, string>();
        foreach (var split in new[] { "training", "validation", "test" })
        {
            var fileName = split + ".csv";
            var path = Path.Combine(directory, fileName);
            var hash = CategoryModelStore.HashFile(path);
            Require(hash == root.GetProperty("artifacts_sha256").GetProperty(fileName).GetString(), $"Dataset checksum mismatch: {fileName}.");
            hashes[fileName] = hash;
            var rows = ReadCsv(path);
            Require(rows.Length > 0 && rows.Select(row => row.Category).Distinct().Order().SequenceEqual(Enum.GetNames<TicketCategory>().Order()), $"Every category must be present in {split}.");
            foreach (var row in rows)
            {
                Require(ids.Add(row.Id), "Ticket IDs must be unique across all splits.");
                Require(!owners.TryGetValue(row.ScenarioId, out var owner) || owner == split, "A scenario is shared between dataset splits.");
                owners[row.ScenarioId] = split;
                var normalized = string.Join(' ', (row.Title + " " + row.Description).Normalize().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                Require(text.Add(normalized), "Duplicate ticket text across the dataset.");
            }
            Require(rows.GroupBy(row => row.ScenarioId).All(group => group.Select(row => row.Category).Distinct().Count() == 1), "Conflicting scenario labels.");
            splits.Add(rows);
        }
        Require(ids.Count == root.GetProperty("rows").GetInt32() && owners.Count == root.GetProperty("scenario_groups").GetInt32(), "Dataset counts differ from the manifest.");
        hashes["manifest.json"] = CategoryModelStore.HashFile(Path.Combine(directory, "manifest.json"));
        return new TicketDataset(root.GetProperty("dataset_version").GetString()!, hashes, splits[0], splits[1], splits[2]);
    }

    private static DatasetTicket[] ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        Require(parser.ReadFields()?.SequenceEqual(Columns) == true, "Unexpected CSV header.");
        var rows = new List<DatasetTicket>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? throw new InvalidDataException("Missing CSV record.");
            Require(fields.Length == Columns.Length && fields.All(value => !string.IsNullOrWhiteSpace(value)), "Missing or extra CSV fields.");
            Require(Guid.TryParse(fields[0], out var id) && id != Guid.Empty, "Invalid ticket ID.");
            Require(Guid.TryParse(fields[1], out var scenario) && scenario != Guid.Empty, "Invalid scenario ID.");
            Require(fields[2].Length <= 160 && fields[3].Length <= 8000, "Text exceeds the ticket input limits.");
            Require(Enum.GetNames<TicketCategory>().Contains(fields[4], StringComparer.Ordinal), "Unknown category label.");
            rows.Add(new DatasetTicket(id, scenario, fields[2], fields[3], fields[4]) { Priority = fields[5], RecommendedTeam = fields[6] });
        }
        return rows.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
