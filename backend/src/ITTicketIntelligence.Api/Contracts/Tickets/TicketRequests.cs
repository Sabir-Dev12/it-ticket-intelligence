using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Api.Contracts.Tickets;

public sealed record CreateTicketRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(8000)] public required string Description { get; init; }
    public required Guid CreatedById { get; init; }
    public TicketCategory? Category { get; init; }
    public TicketPriority? Priority { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public Guid? AssignedUserId { get; init; }
}

public sealed record UpdateTicketRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(8000)] public required string Description { get; init; }
    public TicketCategory? Category { get; init; }
    public TicketPriority? Priority { get; init; }
    public required int Revision { get; init; }
    public TicketAssignmentRequest? Assignment { get; init; }
    public TicketUserAssignmentRequest? Assignee { get; init; }
}
public sealed record TicketUserAssignmentRequest { public required Guid? UserId { get; init; } }

public sealed record TicketAssignmentRequest { public required Guid? TeamId { get; init; } }

public sealed record ChangeTicketStatusRequest
{
    public required TicketStatus Status { get; init; }
    [StringLength(12000)] public string? Resolution { get; init; }
    public required int Revision { get; init; }
}

public sealed record AssignTicketTeamRequest
{
    public required Guid? TeamId { get; init; }
    public required int Revision { get; init; }
}

public sealed record AddTicketCommentRequest
{
    public required Guid AuthorId { get; init; }
    [Required, StringLength(4000)] public required string Message { get; init; }
    public required int Revision { get; init; }
}

public sealed record RestoreTicketRequest
{
    public required int Revision { get; init; }
}

public sealed class GetTicketsRequest
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    public TicketStatus? Status { get; init; }
    public TicketPriority? Priority { get; init; }
    public TicketCategory? Category { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public Guid? CreatedById { get; init; }
    public bool IncludeArchived { get; init; }
}
