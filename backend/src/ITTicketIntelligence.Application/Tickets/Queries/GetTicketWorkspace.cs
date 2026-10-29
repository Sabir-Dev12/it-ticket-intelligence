using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Validation;

namespace ITTicketIntelligence.Application.Tickets.Queries;

public sealed record TeamOptionDto(Guid Id, string Name, bool IsActive);
public sealed record SavedTicketAnalysis(TicketAgentResult Result, DateTimeOffset CreatedAt);
public sealed record TicketWorkspaceDto(TeamOptionDto? AssignedTeam, IReadOnlyList<TeamOptionDto> AssignableTeams,
    bool TeamsTruncated, SavedTicketAnalysis? Analysis);
public sealed record GetTicketWorkspaceQuery(Guid TicketId);

public interface ITicketWorkspaceReader
{
    Task<TicketWorkspaceDto?> ReadAsync(Guid ticketId, CancellationToken cancellationToken);
}

public sealed class GetTicketWorkspaceHandler(ITicketWorkspaceReader reader) : IQueryHandler<GetTicketWorkspaceQuery, TicketWorkspaceDto>
{
    public async Task<TicketWorkspaceDto> HandleAsync(GetTicketWorkspaceQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new TicketValidation().Id(query.TicketId, "ticketId").ThrowIfInvalid();
        return await reader.ReadAsync(query.TicketId, cancellationToken) ?? throw new ResourceNotFoundException("Ticket");
    }
}
