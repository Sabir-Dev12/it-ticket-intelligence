using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Tickets.Commands;

public sealed record TicketAssignment(Guid? TeamId);
public sealed record CreateTicketCommand(string Title, string Description, Guid CreatedById, TicketCategory? Category = null, TicketPriority? Priority = null, Guid? AssignedTeamId = null, Guid? AssignedUserId = null);
public sealed record UpdateTicketCommand(Guid Id, string Title, string Description, TicketCategory? Category, TicketPriority? Priority, int Revision, TicketAssignment? Assignment = null, TicketUserAssignment? Assignee = null);
public sealed record TicketUserAssignment(Guid? UserId);
public sealed record ChangeTicketStatusCommand(Guid Id, TicketStatus Status, string? Resolution, int Revision);
public sealed record AssignTicketTeamCommand(Guid Id, Guid? TeamId, int Revision);
public sealed record AddTicketCommentCommand(Guid Id, Guid AuthorId, string Message, int Revision);
public sealed record ArchiveTicketCommand(Guid Id, int Revision);
public sealed record RestoreTicketCommand(Guid Id, int Revision);
