using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class AIConversationRepository(AppDbContext db) : IAIConversationRepository
{
    public async Task<IReadOnlyList<AIConversation>> ListOwnedChatsAsync(Guid owner,int page,CancellationToken cancellationToken=default)=>
        await db.AIConversations.AsNoTracking().Where(c=>c.UserId==owner && c.TicketId==null).OrderByDescending(c=>c.UpdatedAt).ThenByDescending(c=>c.Id).Skip((page-1)*20).Take(21).ToListAsync(cancellationToken);
    public Task<AIConversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AIConversations.Include(conversation => conversation.Messages.OrderBy(message => EF.Property<int>(message, "Position")))
            .SingleOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);

    public void Add(AIConversation conversation) => db.AIConversations.Add(conversation);
}
