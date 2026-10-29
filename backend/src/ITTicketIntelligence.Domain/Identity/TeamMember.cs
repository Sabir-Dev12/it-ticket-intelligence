using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Identity;

public sealed class TeamMember
{
    public TeamMember(Guid teamId, Guid userId, DateTimeOffset createdAt)
    {
        TeamId = Guard.Id(teamId, nameof(teamId));
        UserId = Guard.Id(userId, nameof(userId));
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
    }

    public Guid TeamId { get; }
    public Guid UserId { get; }
    public DateTimeOffset CreatedAt { get; }
}
