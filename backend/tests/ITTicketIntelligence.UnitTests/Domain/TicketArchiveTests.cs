using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class TicketArchiveTests
{
    [Fact]
    public void ArchivedTicket_RejectsMutationsUntilRestored()
    {
        var ticket = DomainTestData.Ticket();
        ticket.Archive(DomainTestData.Now);

        Assert.Throws<DomainException>(() => ticket.StartWork(DomainTestData.Now));
        Assert.Throws<DomainException>(() => ticket.SetClassification(TicketCategory.Database, TicketPriority.High, DomainTestData.Now));
        Assert.Throws<DomainException>(() => ticket.AssignTo(Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => ticket.AddComment(new TicketComment(Guid.NewGuid(), ticket.Id, Guid.NewGuid(), "Message", DomainTestData.Now)));
        Assert.Throws<DomainException>(() => ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database,
            new PredictionConfidence(.9), "v1", 1, DomainTestData.Now)));
        ticket.Restore(DomainTestData.Now);
        ticket.StartWork(DomainTestData.Now);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void ArchiveAndRestore_DoNotReopenAClosedTicket()
    {
        var ticket = DomainTestData.Ticket();
        ticket.StartWork(DomainTestData.Now);
        ticket.Resolve("The service was restored.", DomainTestData.Now);
        ticket.Close(DomainTestData.Now);
        ticket.Archive(DomainTestData.Now);
        ticket.Restore(DomainTestData.Now);
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.NotNull(ticket.Resolution);
        Assert.NotNull(ticket.ClosedAt);
        Assert.Null(ticket.ArchivedAt);
    }

    [Fact]
    public void Archive_RejectsBackdatedChangesWithoutPartialMutation()
    {
        var ticket = DomainTestData.Ticket();
        Assert.Throws<DomainException>(() => ticket.Archive(DomainTestData.Now.AddMinutes(-1)));
        Assert.False(ticket.IsArchived);
        Assert.Null(ticket.ArchivedAt);
        Assert.Equal(1, ticket.Revision);
    }
}
