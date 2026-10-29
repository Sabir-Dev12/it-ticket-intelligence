namespace ITTicketIntelligence.Infrastructure.Persistence.Agents;

internal sealed class AgentAnalysisRecord
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public int TicketRevision { get; set; }
    public string ResultJson { get; set; } = "";
    public string ResultHash { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
