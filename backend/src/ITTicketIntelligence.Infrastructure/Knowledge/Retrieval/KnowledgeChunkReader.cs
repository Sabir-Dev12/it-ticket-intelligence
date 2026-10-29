using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;

internal sealed class KnowledgeChunkReader(AppDbContext db, IKnowledgeAccessContext access) : IKnowledgeChunkReader
{
    public async Task<IReadOnlyList<KnowledgeChunkDto>> ReadCurrentAsync(IReadOnlyList<Guid> chunkIds, CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0) return [];
        return await (from c in db.KnowledgeChunks.AsNoTracking()
            join d in db.KnowledgeDocuments.AsNoTracking() on c.DocumentId equals d.Id
            where access.CanSearch && (!d.IsRestricted || access.IsAdmin || (access.UserId.HasValue && d.CreatedById==access.UserId)) && chunkIds.Contains(c.Id) && c.DocumentVersion == d.Version && d.ProcessedAt != null
            select new KnowledgeChunkDto(c.Id, c.Content, new KnowledgeChunkMetadata(d.Id, c.DocumentVersion, d.Title,
                d.Source, d.Category, d.Kind, d.Format, c.Position, c.StartOffset, c.EndOffset, c.ContentHash)))
            .ToListAsync(cancellationToken);
    }
}
