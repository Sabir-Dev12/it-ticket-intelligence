using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Domain.Knowledge;

public sealed class KnowledgeDocument : Entity
{
    private readonly List<KnowledgeChunk> chunks = [];

    public KnowledgeDocument(Guid id, string title, string content, string source, TicketCategory category,
        DateTimeOffset createdAt, Guid? createdById = null, KnowledgeDocumentKind kind = KnowledgeDocumentKind.Document,
        KnowledgeContentFormat format = KnowledgeContentFormat.PlainText) : base(id)
    {
        Title = Guard.RequiredText(title, nameof(title), 160);
        Guard.RequiredText(content, nameof(content), 100000);
        if (content.Length > 100000) throw new DomainException("Content must not exceed 100000 characters.");
        Content = content;
        Source = Guard.RequiredText(source, nameof(source), 512);
        Category = Guard.Defined(category, nameof(category));
        Kind = Guard.Defined(kind, nameof(kind));
        Format = Guard.Defined(format, nameof(format));
        CreatedById = Guard.OptionalId(createdById, nameof(createdById));
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public string Title { get; private set; }
    public string Content { get; private set; }
    public string Source { get; private set; }
    public TicketCategory Category { get; private set; }
    public Guid? CreatedById { get; }
    public bool IsRestricted { get; private set; }
    public void SetRestricted(bool restricted,DateTimeOffset changedAt)
    { UpdatedAt=Guard.NotBefore(changedAt,UpdatedAt,nameof(changedAt));IsRestricted=restricted; }
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public KnowledgeDocumentKind Kind { get; private set; }
    public KnowledgeContentFormat Format { get; private set; }
    public string? CleanedContent { get; private set; }
    public string? ProcessingVersion { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public IReadOnlyCollection<KnowledgeChunk> Chunks => chunks.AsReadOnly();

    public void Update(string title, string content, string source, TicketCategory category, DateTimeOffset changedAt,
        KnowledgeDocumentKind? kind = null, KnowledgeContentFormat? format = null)
    {
        var validTitle = Guard.RequiredText(title, nameof(title), 160);
        Guard.RequiredText(content, nameof(content), 100000);
        if (content.Length > 100000) throw new DomainException("Content must not exceed 100000 characters.");
        var validContent = content;
        var validSource = Guard.RequiredText(source, nameof(source), 512);
        var validCategory = Guard.Defined(category, nameof(category));
        var validKind = Guard.Defined(kind ?? Kind, nameof(kind));
        var validFormat = Guard.Defined(format ?? Format, nameof(format));
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        if (Version == int.MaxValue)
        {
            throw new DomainException("The document version limit has been reached.");
        }

        Title = validTitle;
        Content = validContent;
        Source = validSource;
        Category = validCategory;
        Kind = validKind;
        Format = validFormat;
        CleanedContent = null;
        ProcessingVersion = null;
        ProcessedAt = null;
        chunks.Clear();
        Version++;
        UpdatedAt = timestamp;
    }

    public void ApplyPreparation(string cleanedContent, string processingVersion,
        IReadOnlyList<KnowledgeChunk> preparedChunks, DateTimeOffset processedAt)
    {
        Guard.RequiredText(cleanedContent, nameof(cleanedContent), 100000);
        if (cleanedContent.Length > 100000) throw new DomainException("Cleaned content must not exceed 100000 characters.");
        var cleaned = cleanedContent;
        var processor = Guard.RequiredText(processingVersion, nameof(processingVersion), 64);
        var timestamp = Guard.NotBefore(processedAt, UpdatedAt, nameof(processedAt));
        ArgumentNullException.ThrowIfNull(preparedChunks);
        preparedChunks = preparedChunks.ToArray();
        if (preparedChunks.Count == 0 || preparedChunks.Select(c => c.Id).Distinct().Count() != preparedChunks.Count)
            throw new DomainException("A prepared document requires uniquely identified chunks.");
        var previousEnd = 0;
        for (var i = 0; i < preparedChunks.Count; i++)
        {
            var chunk = preparedChunks[i];
            if (chunk.DocumentId != Id || chunk.DocumentVersion != Version || chunk.Position != i ||
                chunk.StartOffset > previousEnd || chunk.EndOffset <= previousEnd || chunk.EndOffset > cleaned.Length ||
                chunk.CreatedAt != timestamp || cleaned[chunk.StartOffset..chunk.EndOffset] != chunk.Content)
                throw new DomainException("Chunks must cover the current document in order, with matching content and metadata.");
            previousEnd = chunk.EndOffset;
        }
        if (previousEnd != cleaned.Length) throw new DomainException("Chunks must cover the complete cleaned document.");
        CleanedContent = cleaned;
        ProcessingVersion = processor;
        ProcessedAt = timestamp;
        chunks.Clear();
        chunks.AddRange(preparedChunks);
    }
}
