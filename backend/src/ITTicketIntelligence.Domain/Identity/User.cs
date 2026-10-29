using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.ValueObjects;

namespace ITTicketIntelligence.Domain.Identity;

public sealed class User : Entity
{
    public User(Guid id, string name, EmailAddress email, UserRole role, DateTimeOffset createdAt) : base(id)
    {
        ArgumentNullException.ThrowIfNull(email);
        Name = Guard.RequiredText(name, nameof(name), 100);
        Email = email;
        Role = Guard.Defined(role, nameof(role));
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public string Name { get; private set; }
    public EmailAddress Email { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateProfile(string name, EmailAddress email, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(email);
        var validName = Guard.RequiredText(name, nameof(name), 100);
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        Name = validName;
        Email = email;
        UpdatedAt = timestamp;
    }

    public void ChangeRole(UserRole role, DateTimeOffset changedAt)
    {
        var validRole = Guard.Defined(role, nameof(role));
        var timestamp = Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
        Role = validRole;
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
