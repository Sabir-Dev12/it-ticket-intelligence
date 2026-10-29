using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Conversations;

public sealed class AIConversation : Entity
{
    private readonly List<AIMessage> messages = [];

    public AIConversation(Guid id, Guid userId, DateTimeOffset createdAt, Guid? ticketId = null, string? title = null) : base(id)
    {
        UserId = Guard.Id(userId, nameof(userId));
        TicketId = Guard.OptionalId(ticketId, nameof(ticketId));
        Title = Guard.OptionalText(title, nameof(title), 160);
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public Guid UserId { get; }
    public Guid? TicketId { get; }
    public string? Title { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public IReadOnlyCollection<AIMessage> Messages => messages.AsReadOnly();

    public void Rename(string? title, DateTimeOffset changedAt)
    {
        var validTitle = Guard.OptionalText(title, nameof(title), 160);
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        Title = validTitle;
        UpdatedAt = timestamp;
    }

    public void AddMessage(AIMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ConversationId != Id || messages.Any(existing => existing.Id == message.Id))
        {
            throw new DomainException("A message must belong to this conversation and have a unique identifier.");
        }

        var timestamp = Guard.NotBefore(message.CreatedAt, UpdatedAt, nameof(message.CreatedAt));
        messages.Add(message);
        LastMessageAt = timestamp;
        UpdatedAt = timestamp;
    }
}
