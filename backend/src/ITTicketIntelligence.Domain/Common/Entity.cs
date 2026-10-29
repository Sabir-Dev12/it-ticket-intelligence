namespace ITTicketIntelligence.Domain.Common;

public abstract class Entity
{
    protected Entity(Guid id)
    {
        Id = Guard.Id(id, nameof(id));
    }

    public Guid Id { get; }
}
