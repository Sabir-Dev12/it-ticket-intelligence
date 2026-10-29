using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Validation;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Application.Realtime;

namespace ITTicketIntelligence.Application.Tickets.Commands;

public sealed class TicketCommandHandler(ITicketRepository tickets, ITicketReferenceData references,
    IAuditLogWriter audit, IUnitOfWork unitOfWork, TimeProvider clock, TicketEventNotifier notifications) :
    ICommandHandler<CreateTicketCommand, TicketDto>,
    ICommandHandler<UpdateTicketCommand, TicketDto>,
    ICommandHandler<ChangeTicketStatusCommand, TicketDto>,
    ICommandHandler<AssignTicketTeamCommand, TicketDto>,
    ICommandHandler<AddTicketCommentCommand, AddCommentResult>,
    ICommandHandler<ArchiveTicketCommand, TicketDto>,
    ICommandHandler<RestoreTicketCommand, TicketDto>
{
    public async Task<TicketDto> HandleAsync(CreateTicketCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Text(command.Title, "title", 160).Text(command.Description, "description", 8000)
            .Id(command.CreatedById, "createdById").OptionalEnum(command.Category, "category").OptionalEnum(command.Priority, "priority")
            .OptionalId(command.AssignedTeamId, "assignedTeamId")
            .OptionalId(command.AssignedUserId, "assignedUserId")
            .Require(command.Category.HasValue == command.Priority.HasValue, "category", "Category and priority must both be set or both be empty.").ThrowIfInvalid();
        await RequireActiveUser(command.CreatedById, cancellationToken);
        await RequireTeam(command.AssignedTeamId, cancellationToken);
        await RequireAssignee(command.AssignedUserId, cancellationToken);
        var now = clock.GetUtcNow();
        var ticket = new Ticket(Guid.NewGuid(), await tickets.NextReferenceAsync(cancellationToken),
            command.Title, command.Description, command.CreatedById, now);
        tickets.Add(ticket);
        if (command.Category.HasValue) ticket.SetClassification(command.Category.Value, command.Priority!.Value, now);
        if (command.AssignedTeamId.HasValue) ticket.AssignTo(command.AssignedTeamId.Value, now);
        if (command.AssignedUserId.HasValue) ticket.AssignToUser(command.AssignedUserId.Value, now);
        await Save(ticket, "TicketCreated", "Ticket créé.", command.CreatedById, now, cancellationToken);
        return ticket.ToDto();
    }

    public async Task<TicketDto> HandleAsync(UpdateTicketCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Text(command.Title, "title", 160).Text(command.Description, "description", 8000)
            .OptionalEnum(command.Category, "category").OptionalEnum(command.Priority, "priority")
            .OptionalId(command.Assignee?.UserId, "assignee.userId")
            .Require(command.Category.HasValue == command.Priority.HasValue, "category", "Catégorie et priorité doivent être toutes deux renseignées ou absentes.")
            .OptionalId(command.Assignment?.TeamId, "assignment.teamId").ThrowIfInvalid();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        if (command.Assignment is not null) await RequireTeam(command.Assignment.TeamId, cancellationToken);
        if (command.Assignee is not null) await RequireAssignee(command.Assignee.UserId, cancellationToken);
        var now = clock.GetUtcNow();
        ticket.Update(command.Title, command.Description, command.Category, command.Priority, now);
        if (command.Assignee is { } assignee && assignee.UserId != ticket.AssignedUserId)
        {
            ticket.AssignToUser(assignee.UserId, now);
            audit.Add(new AuditLog(Guid.NewGuid(), null, "TicketAssigned", "Ticket", ticket.Id, now, "Individual assignee updated."));
        }
        if (command.Assignment is { } assignment && assignment.TeamId != ticket.AssignedTeamId)
        {
            if (assignment.TeamId.HasValue) ticket.AssignTo(assignment.TeamId.Value, now); else ticket.Unassign(now);
            audit.Add(new AuditLog(Guid.NewGuid(), null, "TicketAssigned", "Ticket", ticket.Id, now, "Assignment confirmed in ticket form."));
        }
        await Save(ticket, "TicketUpdated", "Description et classification mises à jour.", null, now, cancellationToken);
        return ticket.ToDto();
    }

    public async Task<TicketDto> HandleAsync(ChangeTicketStatusCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = new TicketValidation().Enum(command.Status, "status").OptionalText(command.Resolution, "resolution", 12000);
        if (command.Status == TicketStatus.Resolved) validation.Text(command.Resolution, "resolution", 12000);
        else validation.Require(command.Resolution is null, "resolution", "La résolution concerne uniquement le passage à Resolved.");
        validation.ThrowIfInvalid();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        var previous = ticket.Status;
        var now = clock.GetUtcNow();
        switch (command.Status)
        {
            case TicketStatus.InProgress when ticket.Status == TicketStatus.Resolved: ticket.Reopen(now); break;
            case TicketStatus.InProgress: ticket.StartWork(now); break;
            case TicketStatus.Resolved: ticket.Resolve(command.Resolution!, now); break;
            case TicketStatus.Closed: ticket.Close(now); break;
            default: throw new ConflictException("Le retour au statut Open n’est pas autorisé.");
        }
        await Save(ticket, "TicketStatusChanged", $"{previous} → {ticket.Status}", null, now, cancellationToken);
        return ticket.ToDto();
    }

    public async Task<TicketDto> HandleAsync(AssignTicketTeamCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().OptionalId(command.TeamId, "teamId").ThrowIfInvalid();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        if (command.TeamId.HasValue)
        {
            var team = await references.FindTeamAsync(command.TeamId.Value, cancellationToken)
                ?? throw new ResourceNotFoundException("Équipe");
            if (!team.IsActive) throw new ConflictException("L’équipe est désactivée.");
        }
        var now = clock.GetUtcNow();
        if (command.TeamId.HasValue) ticket.AssignTo(command.TeamId.Value, now);
        else ticket.Unassign(now);
        await Save(ticket, "TicketAssigned", command.TeamId.HasValue ? $"Équipe : {command.TeamId}" : "Affectation supprimée.", null, now, cancellationToken);
        return ticket.ToDto();
    }

    public async Task<AddCommentResult> HandleAsync(AddTicketCommentCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Id(command.AuthorId, "authorId").Text(command.Message, "message", 4000).ThrowIfInvalid();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        await RequireActiveUser(command.AuthorId, cancellationToken);
        var now = clock.GetUtcNow();
        var comment = new TicketComment(Guid.NewGuid(), ticket.Id, command.AuthorId, command.Message, now);
        ticket.AddComment(comment);
        await Save(ticket, "TicketCommentAdded", $"Commentaire : {comment.Id}", command.AuthorId, now, cancellationToken);
        return new AddCommentResult(comment.ToDto(), ticket.Revision);
    }

    public async Task<TicketDto> HandleAsync(ArchiveTicketCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        if (!ticket.IsArchived)
        {
            var now = clock.GetUtcNow();
            ticket.Archive(now);
            await Save(ticket, "TicketArchived", "Ticket archivé ; historique conservé.", null, now, cancellationToken);
        }
        return ticket.ToDto();
    }

    public async Task<TicketDto> HandleAsync(RestoreTicketCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ticket = await Current(command.Id, command.Revision, cancellationToken);
        if (ticket.IsArchived)
        {
            var now = clock.GetUtcNow();
            ticket.Restore(now);
            await Save(ticket, "TicketRestored", "Ticket restauré.", null, now, cancellationToken);
        }
        return ticket.ToDto();
    }

    private async Task<Ticket> Current(Guid id, int revision, CancellationToken cancellationToken)
    {
        new TicketValidation().Id(id, "id").Revision(revision).ThrowIfInvalid();
        var ticket = await tickets.GetByIdAsync(id, cancellationToken) ?? throw new ResourceNotFoundException("Ticket");
        if (ticket.Revision != revision) throw new ConflictException("Le ticket a changé. Actualisez sa révision avant de réessayer.");
        return ticket;
    }

    private async Task RequireAssignee(Guid? id, CancellationToken ct)
    {
        if (id is null) return;
        var user = await references.FindUserAsync(id.Value, ct) ?? throw new ResourceNotFoundException("Assignee");
        if (!user.IsActive || user.Role is not (UserRole.Agent or UserRole.Support))
            throw new ConflictException("Choose an active Agent or Support account.");
    }

    private async Task RequireTeam(Guid? id, CancellationToken ct)
    {
        if (id is null) return;
        var team = await references.FindTeamAsync(id.Value, ct) ?? throw new ResourceNotFoundException("Team");
        if (!team.IsActive) throw new ConflictException("The team is inactive.");
    }

    private async Task RequireActiveUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await references.FindUserAsync(id, cancellationToken) ?? throw new ResourceNotFoundException("Utilisateur");
        if (!user.IsActive) throw new ConflictException("L’utilisateur est désactivé.");
    }

    private async Task Save(Ticket ticket, string action, string details, Guid? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        audit.Add(new AuditLog(Guid.NewGuid(), userId, action, "Ticket", ticket.Id, now, details));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var eventName = action switch
        {
            "TicketCreated" => TicketEventName.TicketCreated,
            "TicketAssigned" => TicketEventName.TicketAssigned,
            "TicketStatusChanged" => TicketEventName.TicketStatusChanged,
            "TicketCommentAdded" => TicketEventName.CommentAdded,
            _ => TicketEventName.TicketUpdated
        };
        await notifications.PublishAsync(eventName, ticket.Id, ticket.Revision, now);
    }
}
