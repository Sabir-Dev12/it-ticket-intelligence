using ITTicketIntelligence.Application.Agents;

namespace ITTicketIntelligence.Application.Assistant;

public sealed record AssistantIdentity(Guid UserId, bool AllTickets, IReadOnlySet<Guid> TicketIds)
{
    public bool CanRead(Guid ticketId) => AllTickets || TicketIds.Contains(ticketId);
}
public sealed record AssistantTicket(Guid Id, string Reference);
public sealed record AssistantMessage(Guid Id, string Role, string Content, DateTimeOffset CreatedAt);
public sealed record AssistantActivity(string CallId, string Name, string State, DateTimeOffset OccurredAt);
public sealed record AssistantTurn(Guid Id, Guid RunId, string State, AssistantMessage UserMessage,
    AssistantMessage? AssistantMessage, TicketAgentResult? Analysis, string? ErrorCode,
    IReadOnlyList<AssistantActivity> Activity, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AssistantConversation(Guid Id, Guid TicketId, string TicketReference, int TicketRevision, string Title,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<AssistantTurn> Turns);
public sealed record AssistantConversationSummary(Guid Id, string Title, Guid TicketId, string TicketReference,
    DateTimeOffset UpdatedAt);
public sealed record AssistantConversationPage(IReadOnlyList<AssistantConversationSummary> Items, int Page, bool HasMore);
public sealed record AssistantTurnStart(bool ShouldRun, Guid RunId, Guid TicketId, string Content,
    IReadOnlyList<AgentMessage> Memory);

public interface IAssistantStore
{
    Task<AssistantTicket?> FindTicketAsync(Guid? id, string? reference, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantConversation> CreateAsync(Guid id, AssistantTicket ticket, string title, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantConversationPage> ListAsync(AssistantIdentity caller, int page, CancellationToken ct);
    Task<AssistantConversation> GetAsync(Guid id, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantTurnStart> BeginAsync(Guid conversationId, Guid requestId, string content, AssistantIdentity caller, CancellationToken ct);
    Task FinishAsync(Guid conversationId, Guid requestId, TicketAgentResult? result, string? errorCode, AssistantIdentity caller, CancellationToken ct);
}
public interface IAssistantService
{
    Task<AssistantConversation> CreateAsync(Guid requestId, string question, Guid? ticketId, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantConversation> SendAsync(Guid conversationId, Guid requestId, string content, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantConversation> GetAsync(Guid id, AssistantIdentity caller, CancellationToken ct);
    Task<AssistantConversationPage> ListAsync(AssistantIdentity caller, int page, CancellationToken ct);
}
public sealed class AssistantAccessDeniedException : Exception;
