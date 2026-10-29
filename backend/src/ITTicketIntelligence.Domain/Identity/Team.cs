using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.Identity;

public sealed class Team : Entity
{
    private readonly List<TeamMember> members = [];

    public Team(Guid id, string name, DateTimeOffset createdAt, string? description = null) : base(id)
    {
        Name = Guard.RequiredText(name, nameof(name), 100);
        Description = Guard.OptionalText(description, nameof(description), 1000);
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<Guid> MemberIds => members.Select(member => member.UserId).ToList().AsReadOnly();
    public IReadOnlyCollection<TeamMember> Members => members.AsReadOnly();

    public void UpdateDetails(string name, string? description, DateTimeOffset changedAt)
    {
        var validName = Guard.RequiredText(name, nameof(name), 100);
        var validDescription = Guard.OptionalText(description, nameof(description), 1000);
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        Name = validName;
        Description = validDescription;
        UpdatedAt = timestamp;
    }

    public void AddMember(Guid userId, DateTimeOffset changedAt)
    {
        Guard.Id(userId, nameof(userId));
        if (!IsActive || members.Any(member => member.UserId == userId))
        {
            throw new DomainException("Members can only be added once and to an active team.");
        }

        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        members.Add(new TeamMember(Id, userId, timestamp));
        UpdatedAt = timestamp;
    }

    public void RemoveMember(Guid userId, DateTimeOffset changedAt)
    {
        Guard.Id(userId, nameof(userId));
        var membership = members.SingleOrDefault(member => member.UserId == userId);
        if (membership is null)
        {
            throw new DomainException("The user is not a member of this team.");
        }

        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        members.Remove(membership);
        UpdatedAt = timestamp;
    }

    public void Deactivate(DateTimeOffset changedAt) => SetActive(false, changedAt);

    public void Reactivate(DateTimeOffset changedAt) => SetActive(true, changedAt);

    private void SetActive(bool active, DateTimeOffset changedAt)
    {
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        IsActive = active;
        UpdatedAt = timestamp;
    }
}
