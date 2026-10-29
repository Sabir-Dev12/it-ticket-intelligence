using System.Security.Cryptography;
using System.Text.Json;
using ITTicketIntelligence.Application.Intelligence;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class TeamModelStore
{
    public const string ModelFileName = "model.zip";
    public const string MetadataFileName = "metadata.json";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string[] GetScoreLabels(ITransformer model, DataViewSchema schema)
    {
        VBuffer<ReadOnlyMemory<char>> labels = default;
        model.GetOutputSchema(schema)["Score"].Annotations.GetValue("SlotNames", ref labels);
        return labels.DenseValues().Select(label => label.ToString()).ToArray();
    }

    public static void Save(string directory, MLContext ml, ITransformer model, DataViewSchema schema, TeamModelMetadata metadata, string evaluationJson)
    {
        var destination = Path.GetFullPath(directory);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Team model version already exists; use a new directory.");
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var modelPath = Path.Combine(staging, ModelFileName);
            ml.Model.Save(model, schema, modelPath);
            File.WriteAllText(Path.Combine(staging, MetadataFileName), JsonSerializer.Serialize(metadata with { ModelSha256 = HashFile(modelPath) }, JsonOptions) + "\n");
            File.WriteAllText(Path.Combine(staging, "evaluation.json"), evaluationJson + "\n");
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static (MLContext Context, ITransformer Model, TeamModelMetadata Metadata) Load(string directory)
    {
        var modelPath = Path.Combine(directory, ModelFileName);
        var metadataPath = Path.Combine(directory, MetadataFileName);
        if (!File.Exists(modelPath) || !File.Exists(metadataPath)) throw new InvalidDataException("Team model artifacts are missing. Run the separate team training service first.");
        var metadata = JsonSerializer.Deserialize<TeamModelMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("Team model metadata is empty.");
        if (metadata.ContractVersion != 1 || string.IsNullOrWhiteSpace(metadata.ModelVersion) || metadata.ModelVersion.Length > 100 ||
            metadata.Features is null || !metadata.Features.SequenceEqual(new[] { "Title", "Description" }) ||
            metadata.Labels is null || metadata.Labels.Length != TicketTeamNames.All.Count ||
            !metadata.Labels.Order().SequenceEqual(TicketTeamNames.All.Order()) || !double.IsFinite(metadata.ReferenceThreshold) || metadata.ReferenceThreshold <= 0 || metadata.ReferenceThreshold > 1)
            throw new InvalidDataException("Team model metadata is incompatible.");
        if (!string.Equals(metadata.ModelSha256, HashFile(modelPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Team model checksum mismatch.");
        var ml = new MLContext(seed: 20261024);
        var model = ml.Model.Load(modelPath, out var schema);
        foreach (var name in new[] { "Title", "Description", "RecommendedTeam" })
            if (schema.GetColumnOrNull(name) is not { } column || column.Type != TextDataViewType.Instance) throw new InvalidDataException("Team model input schema is incompatible.");
        if (schema.GetColumnOrNull("ExampleWeight") is not { } weight || weight.Type != NumberDataViewType.Single || !GetScoreLabels(model, schema).SequenceEqual(metadata.Labels))
            throw new InvalidDataException("Team model weight schema or score slots are incompatible.");
        return (ml, model, metadata);
    }
}
