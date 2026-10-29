using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Auditing;

/// <summary>An immutable fact. A null actor represents a system action.</summary>
public sealed class AuditLog : Entity
{
    public AuditLog(Guid id, Guid? userId, string action, string entityType, Guid entityId,
        DateTimeOffset occurredAt, string? details = null, Guid? correlationId = null) : base(id)
    {
        UserId = Guard.OptionalId(userId, nameof(userId));
        Action = Guard.RequiredText(action, nameof(action), 128);
        EntityType = Guard.RequiredText(entityType, nameof(entityType), 128);
        EntityId = Guard.Id(entityId, nameof(entityId));
        OccurredAt = Guard.Timestamp(occurredAt, nameof(occurredAt));
        Details = Guard.OptionalText(details, nameof(details), 4000);
        CorrelationId = Guard.OptionalId(correlationId, nameof(correlationId));
    }

    public Guid? UserId { get; }
    public string Action { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? Details { get; }
    public Guid? CorrelationId { get; }
}
