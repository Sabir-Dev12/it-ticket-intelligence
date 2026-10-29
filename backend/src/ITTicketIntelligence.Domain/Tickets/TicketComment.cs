using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Tickets;

public sealed class TicketComment : Entity
{
    public TicketComment(Guid id, Guid ticketId, Guid authorId, string message, DateTimeOffset createdAt) : base(id)
    {
        TicketId = Guard.Id(ticketId, nameof(ticketId));
        AuthorId = Guard.Id(authorId, nameof(authorId));
        Message = Guard.RequiredText(message, nameof(message), 4000);
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
    }

    public Guid TicketId { get; }
    public Guid AuthorId { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
}
