using System.Security.Cryptography;
using System.Text;

namespace ITTicketIntelligence.Application.Knowledge;

public sealed class KnowledgeDocumentProcessor(IKnowledgeTextExtractor extractor) : IKnowledgeDocumentProcessor
{
    public const int ChunkSize = 1200;
    public const int ChunkOverlap = 150;
    public const string ProcessorVersion = "text-v1";

    public async Task<PreparedKnowledgeDocument> PrepareAsync(KnowledgeProcessingInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Id(input.DocumentId).Version(input.DocumentVersion)
            .Content(input.Title, input.Content, input.Source, input.Category, input.Kind, input.Format).ThrowIfInvalid();
        var extracted = await extractor.ExtractAsync(input.Content, input.Format, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string normalized;
        try { normalized = extracted.Normalize(NormalizationForm.FormC).ReplaceLineEndings("\n"); }
        catch (ArgumentException)
        { throw new Common.RequestValidationException(new Dictionary<string, string[]> { ["content"] = ["Le texte Unicode est invalide."] }); }
        var cleaned = new StringBuilder(normalized.Length);
        var previousBlank = true;
        foreach (var line in normalized.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filtered = new string(line.Where(c => (!char.IsControl(c) || c == '\t') && c != '\uFEFF').ToArray()).TrimEnd();
            if (filtered.Length == 0 && previousBlank) continue;
            cleaned.Append(filtered).Append('\n');
            previousBlank = filtered.Length == 0;
        }
        var text = cleaned.ToString().TrimEnd('\n');
        new KnowledgeValidation().Require(!string.IsNullOrWhiteSpace(text), "content", "Le document ne contient aucun texte exploitable après extraction.").ThrowIfInvalid();
        var chunks = new List<PreparedKnowledgeChunk>();
        var start = 0;
        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(start + ChunkSize, text.Length);
            if (end < text.Length)
            {
                if (char.IsLowSurrogate(text[end]) && char.IsHighSurrogate(text[end - 1])) end--;
                for (var boundary = end - 1; boundary > start + ChunkSize * 3 / 4; boundary--)
                    if (char.IsWhiteSpace(text[boundary])) { end = boundary + 1; break; }
            }
            var content = text[start..end];
            if (string.IsNullOrWhiteSpace(content))
                throw new Common.RequestValidationException(new Dictionary<string, string[]> { ["content"] = ["Le document contient un segment vide trop long."] });
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            chunks.Add(new PreparedKnowledgeChunk(content, new KnowledgeChunkMetadata(input.DocumentId, input.DocumentVersion,
                input.Title.Trim(), input.Source.Trim(), input.Category, input.Kind, input.Format, chunks.Count, start, end, hash)));
            if (end == text.Length) break;
            start = end - ChunkOverlap;
            if (char.IsLowSurrogate(text[start]) && start > 0 && char.IsHighSurrogate(text[start - 1])) start--;
        }
        return new PreparedKnowledgeDocument(text, ProcessorVersion, chunks.AsReadOnly());
    }
}
