using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Knowledge;

public sealed class KnowledgeChunk : Entity
{
    public KnowledgeChunk(Guid id, Guid documentId, int documentVersion, int position, string content,
        int startOffset, int endOffset, string contentHash, DateTimeOffset createdAt) : base(id)
    {
        DocumentId = Guard.Id(documentId, nameof(documentId));
        if (documentVersion < 1 || position < 0 || startOffset < 0 || endOffset <= startOffset)
            throw new DomainException("Chunk version, position and offsets must be valid.");
        Guard.RequiredText(content, nameof(content), 4000);
        if (content.Length > 4000 || endOffset - startOffset != content.Length)
            throw new DomainException("Chunk offsets must describe its exact content.");
        if (contentHash is null || contentHash.Length != 64 || contentHash.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new DomainException("Chunk content hash must be a lowercase SHA-256 hex string.");
        DocumentVersion = documentVersion;
        Position = position;
        Content = content; // Preserve exact offsets, including boundary whitespace.
        StartOffset = startOffset;
        EndOffset = endOffset;
        ContentHash = contentHash;
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
    }

    public Guid DocumentId { get; }
    public int DocumentVersion { get; }
    public int Position { get; }
    public string Content { get; }
    public int StartOffset { get; }
    public int EndOffset { get; }
    public string ContentHash { get; }
    public DateTimeOffset CreatedAt { get; }
}
