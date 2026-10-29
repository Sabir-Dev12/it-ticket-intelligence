using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Domain.Tickets;

public sealed class Ticket : Entity
{
    private readonly List<TicketComment> comments = [];
    private readonly List<TicketPrediction> predictions = [];

    public Ticket(Guid id, string reference, string title, string description, Guid createdById, DateTimeOffset createdAt) : base(id)
    {
        Reference = Guard.RequiredText(reference, nameof(reference), 32).ToUpperInvariant();
        Title = Guard.RequiredText(title, nameof(title), 160);
        Description = Guard.RequiredText(description, nameof(description), 8000);
        CreatedById = Guard.Id(createdById, nameof(createdById));
        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
        UpdatedAt = CreatedAt;
    }

    public string Reference { get; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public Guid CreatedById { get; }
    public TicketStatus Status { get; private set; } = TicketStatus.Open;
    public TicketCategory? Category { get; private set; }
    public TicketPriority? Priority { get; private set; }
    public Guid? AssignedTeamId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public string? Resolution { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public IReadOnlyCollection<TicketComment> Comments => comments.AsReadOnly();
    public IReadOnlyCollection<TicketPrediction> Predictions => predictions.AsReadOnly();

    public void UpdateDetails(string title, string description, DateTimeOffset changedAt)
    {
        Update(title, description, Category, Priority, changedAt);
    }

    public void Update(string title, string description, TicketCategory? category, TicketPriority? priority, DateTimeOffset changedAt)
    {
        EnsureActive();
        var validTitle = Guard.RequiredText(title, nameof(title), 160);
        var validDescription = Guard.RequiredText(description, nameof(description), 8000);
        if (category.HasValue != priority.HasValue)
        {
            throw new DomainException("Category and priority must either both be set or both be absent.");
        }
        if (category.HasValue) Guard.Defined(category.Value, nameof(category));
        if (priority.HasValue) Guard.Defined(priority.Value, nameof(priority));
        var timestamp = ValidateChange(changedAt);
        Title = validTitle;
        Description = validDescription;
        Category = category;
        Priority = priority;
        Touch(timestamp);
    }

    public void SetClassification(TicketCategory category, TicketPriority priority, DateTimeOffset changedAt)
    {
        EnsureActive();
        var validCategory = Guard.Defined(category, nameof(category));
        var validPriority = Guard.Defined(priority, nameof(priority));
        var timestamp = ValidateChange(changedAt);
        Category = validCategory;
        Priority = validPriority;
        Touch(timestamp);
    }

    public void AssignTo(Guid teamId, DateTimeOffset changedAt)
    {
        EnsureActive();
        var validTeamId = Guard.Id(teamId, nameof(teamId));
        var timestamp = ValidateChange(changedAt);
        AssignedTeamId = validTeamId;
        Touch(timestamp);
    }

    public void AssignToUser(Guid? userId, DateTimeOffset changedAt)
    {
        EnsureActive();
        if (userId.HasValue) Guard.Id(userId.Value, nameof(userId));
        var timestamp = ValidateChange(changedAt);
        AssignedUserId = userId;
        Touch(timestamp);
    }

    public void Unassign(DateTimeOffset changedAt)
    {
        EnsureActive();
        var timestamp = ValidateChange(changedAt);
        AssignedTeamId = null;
        Touch(timestamp);
    }

    public void RecordAttachmentChange(DateTimeOffset changedAt)
    {
        EnsureActive();
        Touch(ValidateChange(changedAt));
    }

    public void StartWork(DateTimeOffset changedAt)
    {
        EnsureStatus(TicketStatus.Open);
        var timestamp = ValidateChange(changedAt);
        Status = TicketStatus.InProgress;
        Touch(timestamp);
    }

    public void Resolve(string resolution, DateTimeOffset changedAt)
    {
        EnsureStatus(TicketStatus.InProgress);
        var validResolution = Guard.RequiredText(resolution, nameof(resolution), 12000);
        var timestamp = ValidateChange(changedAt);
        Resolution = validResolution;
        ResolvedAt = timestamp;
        Status = TicketStatus.Resolved;
        Touch(timestamp);
    }

    public void Close(DateTimeOffset changedAt)
    {
        EnsureStatus(TicketStatus.Resolved);
        var timestamp = ValidateChange(changedAt);
        ClosedAt = timestamp;
        Status = TicketStatus.Closed;
        Touch(timestamp);
    }

    public void Reopen(DateTimeOffset changedAt)
    {
        EnsureStatus(TicketStatus.Resolved);
        var timestamp = ValidateChange(changedAt);
        Resolution = null;
        ResolvedAt = null;
        Status = TicketStatus.InProgress;
        Touch(timestamp);
    }

    public void AddComment(TicketComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        EnsureNotArchived();
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException("A closed ticket cannot receive comments.");
        }

        if (comment.TicketId != Id || comments.Any(existing => existing.Id == comment.Id))
        {
            throw new DomainException("A comment must belong to this ticket and have a unique identifier.");
        }

        var timestamp = ValidateChange(comment.CreatedAt);
        comments.Add(comment);
        Touch(timestamp);
    }

    public void RecordPrediction(TicketPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        EnsureActive();
        if (prediction.TicketId != Id || predictions.Any(existing => existing.Id == prediction.Id))
        {
            throw new DomainException("A prediction must belong to this ticket and have a unique identifier.");
        }

        if (prediction.InputRevision > Revision || prediction.CreatedAt < CreatedAt)
        {
            throw new DomainException("A prediction cannot target a future revision or predate the ticket.");
        }

        // Historical predictions are retained; recording a score never applies a classification or assignment.
        predictions.Add(prediction);
    }

    private DateTimeOffset ValidateChange(DateTimeOffset changedAt)
    {
        if (Revision == int.MaxValue)
        {
            throw new DomainException("The ticket revision limit has been reached.");
        }

        return Guard.NotBefore(changedAt, UpdatedAt, nameof(changedAt));
    }

    private void Touch(DateTimeOffset timestamp)
    {
        Revision++;
        UpdatedAt = timestamp;
    }

    private void EnsureActive()
    {
        EnsureNotArchived();
        if (Status is not (TicketStatus.Open or TicketStatus.InProgress))
        {
            throw new DomainException("Only an open or in-progress ticket can be edited.");
        }
    }

    private void EnsureStatus(TicketStatus expected)
    {
        EnsureNotArchived();
        if (Status != expected)
        {
            throw new DomainException($"This operation requires a ticket in status {expected}.");
        }
    }

    public void Archive(DateTimeOffset changedAt)
    {
        if (IsArchived) return;
        var timestamp = ValidateChange(changedAt);
        IsArchived = true;
        ArchivedAt = timestamp;
        Touch(timestamp);
    }

    public void Restore(DateTimeOffset changedAt)
    {
        if (!IsArchived) return;
        var timestamp = ValidateChange(changedAt);
        IsArchived = false;
        ArchivedAt = null;
        Touch(timestamp);
    }

    private void EnsureNotArchived()
    {
        if (IsArchived) throw new DomainException("Restore the archived ticket before modifying it.");
    }
}
