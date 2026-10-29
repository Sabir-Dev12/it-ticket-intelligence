using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Agents;

public interface ITicketAgent
{
    Task<TicketAgentResult> AnalyzeAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
public sealed record ConversationInvestigation(Guid TicketId, Guid RunId, string Question, IReadOnlyList<AgentMessage> Memory);
public interface IConversationTicketAgent
{
    Task<TicketAgentResult> AnswerAsync(ConversationInvestigation request, CancellationToken cancellationToken = default);
}

// Observations are supplied by the host. All model-written fields are hypotheses or proposals.
public sealed record AgentTicketFacts(Guid Id, int Revision, string Title, string Description,
    TicketStatus Status, bool IsArchived, Guid? AssignedTeamId);
public sealed record AgentHistoryEntry(string Kind, DateTimeOffset OccurredAt, string Description);
public sealed record AgentTeamInformation(Guid Id, string Name, string? Description, bool IsActive, int MemberCount);
public sealed record AgentRecommendedAction(string Description, bool RequiresHumanApproval = true);
public sealed record TicketAgentResult(Guid RunId, int TicketRevision, AgentTicketFacts Facts,
    IntelligenceResult Predictions, string Summary, string Diagnosis, IReadOnlyList<string> PossibleCauses,
    IReadOnlyList<AgentRecommendedAction> RecommendedActions, IReadOnlyList<LLMCitation> Sources,
    double Confidence, bool RequiresHumanReview);

public interface ITicketAgentData
{
    Task<AgentTicketFacts?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentHistoryEntry>> GetHistoryAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<AgentTeamInformation?> GetTeamAsync(string name, CancellationToken cancellationToken);
}

public sealed record AgentAuditEvent(Guid RunId, Guid TicketId, string Action, string Details, DateTimeOffset OccurredAt);
public interface IAgentRunStore
{
    Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken);
    // Atomically checks current ticket/source versions, persists the result and appends completion audit.
    Task CompleteAsync(TicketAgentResult result, string provider, string model, DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public sealed record AgentToolCall(string Id, string Name, string ArgumentsJson);
public sealed record AgentMessage(string Role, string Content, IReadOnlyList<AgentToolCall>? ToolCalls = null,
    string? ToolCallId = null, string? ToolName = null);
public sealed record AgentLLMRequest(IReadOnlyList<AgentMessage> Messages, bool Finalize);
public sealed record AgentDraft(string Summary, string Diagnosis, IReadOnlyList<string> PossibleCauses,
    IReadOnlyList<string> RecommendedActions, IReadOnlyList<string> CitedChunkIds, double Confidence, bool InsufficientContext);
public sealed record AgentLLMTurn(IReadOnlyList<AgentToolCall> ToolCalls, AgentDraft? Draft,
    string Provider, string Model, int Attempts);

/// <summary>LLM transport only. It requests tools but cannot execute them or access persistence.</summary>
public interface ILLMAgentService
{
    Task<AgentLLMTurn> CompleteTurnAsync(AgentLLMRequest request, CancellationToken cancellationToken = default);
}

public sealed record AgentToolResult(string Json, RetrievedContext? Context = null, AgentTicketFacts? Ticket = null);
public interface ITicketAgentTools
{
    Task<AgentToolResult> ExecuteAsync(Guid ticketId, AgentToolCall call, CancellationToken cancellationToken);
}

public sealed class AgentToolDeniedException : Exception
{
    public AgentToolDeniedException() : base("The requested read tool or its arguments are not permitted.") { }
}
public sealed class AgentLimitException : Exception
{
    public AgentLimitException() : base("The ticket agent exceeded its execution budget.") { }
}
