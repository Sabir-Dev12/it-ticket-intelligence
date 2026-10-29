using Microsoft.Extensions.Logging;

namespace ITTicketIntelligence.Application.Realtime;

public enum TicketEventName { TicketCreated, TicketUpdated, TicketAssigned, TicketStatusChanged, PredictionCompleted, AIAnalysisCompleted, CommentAdded }
public sealed record TicketEvent(Guid EventId, TicketEventName Name, Guid TicketId, int TicketRevision,
    DateTimeOffset OccurredAt, Guid? RelatedId = null);

/// <summary>Transport port. Notifications contain invalidation metadata, not confidential ticket content.</summary>
public interface ITicketEventSink
{
    Task SendAsync(TicketEvent notification, CancellationToken cancellationToken);
}
public sealed class NullTicketEventSink : ITicketEventSink
{
    public Task SendAsync(TicketEvent notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Called only after commit. A transport failure must never turn a committed operation into an HTTP failure.</summary>
public sealed class TicketEventNotifier(ITicketEventSink sink, ILogger<TicketEventNotifier> logger)
{
    public async Task PublishAsync(TicketEventName name, Guid ticketId, int revision, DateTimeOffset occurredAt, Guid? relatedId = null)
    {
        var notification = new TicketEvent(Guid.NewGuid(), name, ticketId, revision, occurredAt, relatedId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await sink.SendAsync(notification, timeout.Token);
            logger.LogInformation("Ticket event {EventName} published for {TicketId}, revision {Revision}, event {EventId}", name, ticketId, revision, notification.EventId);
        }
        catch (Exception)
        {
            logger.LogWarning("Ticket event {EventName} could not be delivered for {TicketId}, event {EventId}; committed data is available for resync", name, ticketId, notification.EventId);
        }
    }
}
