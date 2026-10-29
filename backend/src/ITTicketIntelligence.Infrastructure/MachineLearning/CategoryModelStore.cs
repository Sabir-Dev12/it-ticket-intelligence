using System.Security.Cryptography;
using System.Text.Json;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class CategoryModelStore
{
    public const string ModelFileName = "model.zip";
    public const string MetadataFileName = "metadata.json";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string[] GetScoreLabels(ITransformer model, DataViewSchema inputSchema)
    {
        var outputSchema = model.GetOutputSchema(inputSchema);
        VBuffer<ReadOnlyMemory<char>> names = default;
        outputSchema["Score"].Annotations.GetValue("SlotNames", ref names);
        return names.DenseValues().Select(name => name.ToString()).ToArray();
    }

    public static void Save(string directory, MLContext ml, ITransformer model, DataViewSchema schema,
        CategoryModelMetadata metadata, string evaluationJson)
    {
        var destination = Path.GetFullPath(directory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The model version already exists. Choose a new output directory.");
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var modelPath = Path.Combine(staging, ModelFileName);
            ml.Model.Save(model, schema, modelPath);
            var savedMetadata = metadata with { ModelSha256 = HashFile(modelPath) };
            File.WriteAllText(Path.Combine(staging, MetadataFileName), JsonSerializer.Serialize(savedMetadata, JsonOptions) + "\n");
            File.WriteAllText(Path.Combine(staging, "evaluation.json"), evaluationJson + "\n");
            Directory.Move(staging, destination);
        }
        finally
        {
            // staging is a fresh directory created above, never a user-provided existing directory.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static (MLContext Context, ITransformer Model, CategoryModelMetadata Metadata) Load(string directory)
    {
        var metadataPath = Path.Combine(directory, MetadataFileName);
        var modelPath = Path.Combine(directory, ModelFileName);
        if (!File.Exists(metadataPath) || !File.Exists(modelPath))
            throw new InvalidDataException("Category model artifacts are missing. Run the separate training tool before starting the API.");
        var metadata = JsonSerializer.Deserialize<CategoryModelMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("Category model metadata is empty.");
        if (metadata.ContractVersion != 1 || string.IsNullOrWhiteSpace(metadata.ModelVersion) || metadata.ModelVersion.Length > 100 ||
            metadata.Features is null || !metadata.Features.SequenceEqual(new[] { "Title", "Description" }) || metadata.Labels is null ||
            metadata.Labels.Length != Enum.GetValues<TicketCategory>().Length ||
            !metadata.Labels.Order().SequenceEqual(Enum.GetNames<TicketCategory>().Order()))
            throw new InvalidDataException("Category model metadata is incompatible with the prediction contract.");
        if (!string.Equals(metadata.ModelSha256, HashFile(modelPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Category model checksum does not match its metadata.");
        var ml = new MLContext(seed: 20261024);
        var model = ml.Model.Load(modelPath, out var schema);
        foreach (var column in new[] { "Title", "Description", "Category" })
            if (schema.GetColumnOrNull(column) is not { } inputColumn || inputColumn.Type != TextDataViewType.Instance)
                throw new InvalidDataException("Category model input schema is incompatible.");
        if (!GetScoreLabels(model, schema).SequenceEqual(metadata.Labels))
            throw new InvalidDataException("Category score slots do not match the metadata labels.");
        return (ml, model, metadata);
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
