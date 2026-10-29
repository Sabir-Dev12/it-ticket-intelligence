using ITTicketIntelligence.Application.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Realtime;

public sealed class TicketEventNotifierTests
{
    [Theory]
    [InlineData(TicketEventName.TicketCreated)]
    [InlineData(TicketEventName.TicketUpdated)]
    [InlineData(TicketEventName.TicketAssigned)]
    [InlineData(TicketEventName.TicketStatusChanged)]
    [InlineData(TicketEventName.CommentAdded)]
    [InlineData(TicketEventName.PredictionCompleted)]
    [InlineData(TicketEventName.AIAnalysisCompleted)]
    public async Task PublishesAnIdentifiableInvalidationWithoutTicketContents(TicketEventName name)
    {
        var sink = new Sink(); var notifier = new TicketEventNotifier(sink, NullLogger<TicketEventNotifier>.Instance);
        var ticketId = Guid.NewGuid(); var relatedId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await notifier.PublishAsync(name, ticketId, 3, now, relatedId);
        var message = Assert.Single(sink.Messages); Assert.Equal(name, message.Name); Assert.Equal(ticketId, message.TicketId);
        Assert.Equal(3, message.TicketRevision); Assert.Equal(relatedId, message.RelatedId); Assert.NotEqual(Guid.Empty, message.EventId);
    }
    [Fact]
    public async Task DeliveryFailureDoesNotMakeACommittedOperationFail()
    {
        var notifier = new TicketEventNotifier(new Sink { Fail = true }, NullLogger<TicketEventNotifier>.Instance);
        await notifier.PublishAsync(TicketEventName.TicketUpdated, Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
    }
    private sealed class Sink : ITicketEventSink
    {
        public bool Fail { get; init; }
        public List<TicketEvent> Messages { get; } = [];
        public Task SendAsync(TicketEvent notification, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (Fail) throw new InvalidOperationException(); Messages.Add(notification); return Task.CompletedTask; }
    }
}
