using System.Security.Cryptography;
using System.Text.Json;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class PriorityModelStore
{
    public const string CategoryInputPolicy = "provided-at-creation";
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

    public static void Save(string directory, MLContext ml, ITransformer model, DataViewSchema schema,
        PriorityModelMetadata metadata, string evaluationJson)
    {
        var destination = Path.GetFullPath(directory);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Priority model version already exists; choose a new directory.");
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var path = Path.Combine(staging, ModelFileName);
            ml.Model.Save(model, schema, path);
            File.WriteAllText(Path.Combine(staging, MetadataFileName), JsonSerializer.Serialize(metadata with { ModelSha256 = HashFile(path) }, JsonOptions) + "\n");
            File.WriteAllText(Path.Combine(staging, "evaluation.json"), evaluationJson + "\n");
            Directory.Move(staging, destination);
        }
        finally
        {
            // Only this freshly created staging directory is removed on a failed save.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static (MLContext Context, ITransformer Model, PriorityModelMetadata Metadata) Load(string directory)
    {
        var path = Path.Combine(directory, ModelFileName);
        var metadataPath = Path.Combine(directory, MetadataFileName);
        if (!File.Exists(path) || !File.Exists(metadataPath))
            throw new InvalidDataException("Priority model artifacts are missing. Run the separate priority training tool before starting the API.");
        var metadata = JsonSerializer.Deserialize<PriorityModelMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("Priority model metadata is empty.");
        if (metadata.ContractVersion != 1 || string.IsNullOrWhiteSpace(metadata.ModelVersion) || metadata.ModelVersion.Length > 100 ||
            metadata.Features is null || !metadata.Features.SequenceEqual(new[] { "Title", "Description", "Category" }) ||
            metadata.CategoryInputPolicy != CategoryInputPolicy || metadata.Labels is null ||
            metadata.Labels.Length != 4 || !metadata.Labels.Order().SequenceEqual(Enum.GetNames<TicketPriority>().Order()))
            throw new InvalidDataException("Priority model metadata is incompatible with the prediction contract.");
        if (!string.Equals(metadata.ModelSha256, HashFile(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Priority model checksum mismatch.");
        var ml = new MLContext(seed: 20261024);
        var model = ml.Model.Load(path, out var schema);
        foreach (var column in new[] { "Title", "Description", "Category", "Priority" })
            if (schema.GetColumnOrNull(column) is not { } inputColumn || inputColumn.Type != TextDataViewType.Instance)
                throw new InvalidDataException("Priority model input schema is incompatible.");
        if (schema.GetColumnOrNull("ExampleWeight") is not { } weightColumn || weightColumn.Type != NumberDataViewType.Single ||
            !GetScoreLabels(model, schema).SequenceEqual(metadata.Labels))
            throw new InvalidDataException("Priority model weight schema or score labels are incompatible.");
        return (ml, model, metadata);
    }
}
