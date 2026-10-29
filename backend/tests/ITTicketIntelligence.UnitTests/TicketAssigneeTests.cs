using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Tickets;
using Xunit;

namespace ITTicketIntelligence.UnitTests;

public sealed class TicketAssigneeTests
{
    [Fact]
    public void AssigneeIsIndependentOfTeamAndCanBeReplacedOrCleared()
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket(Guid.NewGuid(), "IT-1", "Issue", "Description", Guid.NewGuid(), now);
        var team = Guid.NewGuid(); var person = Guid.NewGuid();
        ticket.AssignTo(team, now);
        ticket.AssignToUser(person, now);
        Assert.Equal(person, ticket.AssignedUserId);
        ticket.AssignToUser(Guid.NewGuid(), now);
        Assert.NotEqual(person, ticket.AssignedUserId);
        ticket.AssignToUser(null, now);
        Assert.Null(ticket.AssignedUserId);
        Assert.Equal(team, ticket.AssignedTeamId);
        Assert.Equal(5, ticket.Revision);
    }

    [Fact]
    public void RejectsEmptyIdentityAndArchivedTicketWithoutChangingAssignment()
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket(Guid.NewGuid(), "IT-1", "Issue", "Description", Guid.NewGuid(), now);
        Assert.Throws<DomainException>(() => ticket.AssignToUser(Guid.Empty, now));
        Assert.Null(ticket.AssignedUserId);
        Assert.Equal(1, ticket.Revision);
        ticket.Archive(now);
        Assert.Throws<DomainException>(() => ticket.AssignToUser(Guid.NewGuid(), now));
    }
}
