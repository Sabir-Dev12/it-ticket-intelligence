using System.Text.RegularExpressions;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.LanguageModels;

namespace ITTicketIntelligence.Application.Assistant;

public sealed partial class AssistantService(IAssistantStore store, IConversationTicketAgent agent) : IAssistantService
{
    public async Task<AssistantConversation> CreateAsync(Guid requestId, string question, Guid? ticketId, AssistantIdentity caller, CancellationToken ct)
    {
        ValidateId(requestId); var content = ValidateContent(question);
        if (ticketId == Guid.Empty) Invalid("ticket", "Choose a valid ticket.");
        var reference = ParseReference(content);
        if (reference is null && ticketId is null) Invalid("ticket", "Mention one ticket, for example: Why is ticket #1045 failing?");
        var ticket = await store.FindTicketAsync(ticketId, reference, caller, ct) ?? throw new ResourceNotFoundException("Ticket");
        if (reference is not null && ticket.Reference != reference) Invalid("ticket", "The selected ticket and the ticket in your question must match.");
        return await store.CreateAsync(requestId, ticket, content[..Math.Min(160, content.Length)], caller, ct);
    }
    public async Task<AssistantConversation> SendAsync(Guid conversationId, Guid requestId, string content, AssistantIdentity caller, CancellationToken ct)
    {
        ValidateId(conversationId); ValidateId(requestId); content = ValidateContent(content);
        var current = await store.GetAsync(conversationId, caller, ct);
        var reference = ParseReference(content);
        if (reference is not null && reference != current.TicketReference) Invalid("ticket", "This conversation belongs to another ticket. Start a new conversation to switch tickets.");
        var turn = await store.BeginAsync(conversationId, requestId, content, caller, ct);
        if (turn.ShouldRun)
        {
            TicketAgentResult? result = null; string? error = null;
            try { result = await agent.AnswerAsync(new(turn.TicketId, turn.RunId, turn.Content, turn.Memory), ct); }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                error = failure switch
                {
                    OperationCanceledException => "Interrupted",
                    ConflictException => "TicketChanged",
                    ResourceNotFoundException => "TicketUnavailable",
                    LLMServiceException { Kind: LLMFailureKind.Timeout } or AgentLimitException => "Timeout",
                    _ => "DependencyUnavailable"
                };
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await store.FinishAsync(conversationId, requestId, result, error, caller, cleanup.Token);
            ct.ThrowIfCancellationRequested();
        }
        return await store.GetAsync(conversationId, caller, ct);
    }
    public Task<AssistantConversation> GetAsync(Guid id, AssistantIdentity caller, CancellationToken ct)
    { ValidateId(id); return store.GetAsync(id, caller, ct); }
    public Task<AssistantConversationPage> ListAsync(AssistantIdentity caller, int page, CancellationToken ct)
    { if (page is < 1 or > 10000) Invalid("page", "Page must be between 1 and 10000."); return store.ListAsync(caller, page, ct); }

    public static string? ParseReference(string content)
    {
        var values = ReferencePattern().Matches(content).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Select(v => "IT-" + v.TrimStart('0')).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 1) Invalid("ticket", "Mention only one ticket per conversation.");
        if (values.Any(v => v == "IT-")) Invalid("ticket", "Choose a valid ticket reference.");
        return values.FirstOrDefault();
    }
    [GeneratedRegex(@"(?i)\bIT-(\d{1,18})\b|(?:\bticket\s*#?\s*|#)(\d{1,18})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
    private static string ValidateContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 2000 || content.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            Invalid("message", "Write a question of 1 to 2000 characters without control characters.");
        return content!.Trim();
    }
    private static void ValidateId(Guid id) { if (id == Guid.Empty) Invalid("id", "A non-empty identifier is required."); }
    private static void Invalid(string field, string message) => throw new RequestValidationException(new Dictionary<string, string[]> { [field] = [message] });
}
