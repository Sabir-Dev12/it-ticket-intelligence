namespace ITTicketIntelligence.Infrastructure.Persistence.Rag;

internal sealed class RagVectorRecord
{
    public Guid ChunkId { get; set; }
    public string Profile { get; set; } = "";
    public int Dimensions { get; set; }
    public float[] Embedding { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
