using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Domain.Knowledge;

namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public interface IKnowledgeRepository
{
    Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<KnowledgePage> GetPageAsync(GetKnowledgePageQuery query, CancellationToken cancellationToken = default);
    Task<bool?> IsAuthorActiveAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(KnowledgeDocument document);
    void Remove(KnowledgeDocument document);
}
