namespace ITTicketIntelligence.Application.LanguageModels;

public sealed record AIChatMessage(string Role, string Content);
public sealed record AIChatHealth(bool Healthy, string Model);
public interface IAIChatService
{
    Task<string> SendAsync(IReadOnlyList<AIChatMessage> messages, string? model, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AIChatMessage> messages, string? model, CancellationToken ct);
    Task<AIChatHealth> HealthAsync(CancellationToken ct);
}
