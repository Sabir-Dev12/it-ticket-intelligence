using ITTicketIntelligence.Domain.Conversations;

namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public interface IAIConversationRepository
{
    Task<IReadOnlyList<AIConversation>> ListOwnedChatsAsync(Guid owner,int page,CancellationToken cancellationToken=default);
    Task<AIConversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(AIConversation conversation);
}
