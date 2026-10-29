namespace ITTicketIntelligence.Infrastructure.Persistence.Assistant;

internal sealed class AssistantTurnRecord
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid RunId { get; set; }
    public Guid UserMessageId { get; set; }
    public Guid? AssistantMessageId { get; set; }
    public Guid? AnalysisId { get; set; }
    public string State { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public DateTimeOffset LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
