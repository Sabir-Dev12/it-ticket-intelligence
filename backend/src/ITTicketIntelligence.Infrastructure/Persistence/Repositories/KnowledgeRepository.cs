using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class KnowledgeRepository(AppDbContext db, ITTicketIntelligence.Application.Knowledge.Retrieval.IKnowledgeAccessContext? access = null) : IKnowledgeRepository
{
    public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.KnowledgeDocuments.Include(document => document.Chunks).SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
    public void Add(KnowledgeDocument document) => db.KnowledgeDocuments.Add(document);
    public void Remove(KnowledgeDocument document) => db.KnowledgeDocuments.Remove(document);
    public Task<bool?> IsAuthorActiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.Where(user => user.Id == id).Select(user => (bool?)user.IsActive).SingleOrDefaultAsync(cancellationToken);
    public async Task<KnowledgePage> GetPageAsync(GetKnowledgePageQuery filter, CancellationToken cancellationToken = default)
    {
        var query = db.KnowledgeDocuments.AsNoTracking().AsQueryable();
        if(access is not null)query=query.Where(d=>access.CanSearch && (!d.IsRestricted || access.IsAdmin || (access.UserId.HasValue && d.CreatedById==access.UserId)));
        if (filter.Category.HasValue) query = query.Where(document => document.Category == filter.Category);
        if (filter.Kind.HasValue) query = query.Where(document => document.Kind == filter.Kind);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = "%" + filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(document => EF.Functions.ILike(document.Title, search, "\\") ||
                EF.Functions.ILike(document.Content, search, "\\") || EF.Functions.ILike(document.Source, search, "\\"));
        }
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(document => document.UpdatedAt).ThenBy(document => document.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(document => new KnowledgeSummaryDto(document.Id, document.Title, document.Source, document.Category,
                document.Kind, document.Format, document.Version, document.UpdatedAt, document.Chunks.Count)).ToListAsync(cancellationToken);
        return new KnowledgePage(items, count, filter.Page, filter.PageSize);
    }
}
