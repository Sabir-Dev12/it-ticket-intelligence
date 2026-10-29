namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public sealed record TicketUserReference(Guid Id, bool IsActive, ITTicketIntelligence.Domain.Enums.UserRole Role = ITTicketIntelligence.Domain.Enums.UserRole.User, string? Name = null);
public sealed record TicketTeamReference(Guid Id, bool IsActive);

public interface ITicketReferenceData
{
    Task<TicketUserReference?> FindUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TicketTeamReference?> FindTeamAsync(Guid id, CancellationToken cancellationToken = default);
}
