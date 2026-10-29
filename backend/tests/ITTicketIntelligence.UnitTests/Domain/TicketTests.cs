using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class TicketTests
{
    [Fact]
    public void NewTicket_IsOpen_WithoutInventedClassificationOrAssignment()
    {
        var ticket = DomainTestData.Ticket();

        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(1, ticket.Revision);
        Assert.Equal(ticket.CreatedAt, ticket.UpdatedAt);
        Assert.Null(ticket.Category);
        Assert.Null(ticket.Priority);
        Assert.Null(ticket.AssignedTeamId);
        Assert.Null(ticket.Resolution);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
        Assert.Empty(ticket.Comments);
        Assert.Empty(ticket.Predictions);
    }

    [Fact]
    public void Lifecycle_TracksResolutionClosureAndRevision()
    {
        var ticket = DomainTestData.Ticket();

        ticket.StartWork(DomainTestData.Now.AddMinutes(1));
        ticket.Resolve("The service account permissions were corrected.", DomainTestData.Now.AddMinutes(2));

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(DomainTestData.Now.AddMinutes(2), ticket.ResolvedAt);
        Assert.NotNull(ticket.Resolution);

        ticket.Close(DomainTestData.Now.AddMinutes(3));

        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Equal(DomainTestData.Now.AddMinutes(3), ticket.ClosedAt);
        Assert.Equal(4, ticket.Revision);
    }

    [Theory]
    [InlineData(TicketStatus.Open, "resolve")]
    [InlineData(TicketStatus.Open, "close")]
    [InlineData(TicketStatus.Open, "reopen")]
    [InlineData(TicketStatus.InProgress, "start")]
    [InlineData(TicketStatus.InProgress, "close")]
    [InlineData(TicketStatus.InProgress, "reopen")]
    [InlineData(TicketStatus.Resolved, "start")]
    [InlineData(TicketStatus.Resolved, "resolve")]
    [InlineData(TicketStatus.Closed, "start")]
    [InlineData(TicketStatus.Closed, "resolve")]
    [InlineData(TicketStatus.Closed, "close")]
    [InlineData(TicketStatus.Closed, "reopen")]
    public void InvalidTransitions_AreRejectedWithoutChangingTheTicket(TicketStatus status, string operation)
    {
        var ticket = AtStatus(status);
        var revision = ticket.Revision;
        var updatedAt = ticket.UpdatedAt;

        Assert.Throws<DomainException>(() =>
        {
            switch (operation)
            {
                case "start": ticket.StartWork(DomainTestData.Now.AddHours(1)); break;
                case "resolve": ticket.Resolve("A valid resolution.", DomainTestData.Now.AddHours(1)); break;
                case "close": ticket.Close(DomainTestData.Now.AddHours(1)); break;
                case "reopen": ticket.Reopen(DomainTestData.Now.AddHours(1)); break;
            }
        });

        Assert.Equal(status, ticket.Status);
        Assert.Equal(revision, ticket.Revision);
        Assert.Equal(updatedAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Reopen_ClearsTheCurrentResolution()
    {
        var ticket = AtStatus(TicketStatus.Resolved);

        ticket.Reopen(DomainTestData.Now.AddMinutes(3));

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.Resolution);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Resolve_RequiresAnExplanation(string? resolution)
    {
        var ticket = AtStatus(TicketStatus.InProgress);

        Assert.Throws<DomainException>(() => ticket.Resolve(resolution!, DomainTestData.Now.AddMinutes(2)));
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public void AnOlderChange_CannotOverwriteNewerState()
    {
        var ticket = DomainTestData.Ticket();
        var team = Guid.NewGuid();
        ticket.AssignTo(team, DomainTestData.Now.AddMinutes(2));

        Assert.Throws<DomainException>(() => ticket.AssignTo(Guid.NewGuid(), DomainTestData.Now.AddMinutes(1)));
        Assert.Equal(team, ticket.AssignedTeamId);
        Assert.Equal(2, ticket.Revision);
    }

    [Fact]
    public void SetClassification_RejectsUndefinedEnumsWithoutPartialMutation()
    {
        var ticket = DomainTestData.Ticket();

        Assert.Throws<DomainException>(() => ticket.SetClassification(TicketCategory.Database, (TicketPriority)999, DomainTestData.Now));
        Assert.Throws<DomainException>(() => ticket.SetClassification((TicketCategory)999, TicketPriority.High, DomainTestData.Now));
        Assert.Null(ticket.Category);
        Assert.Null(ticket.Priority);
        Assert.Equal(1, ticket.Revision);
    }

    [Fact]
    public void ClassificationAndAssignment_AreExplicitChanges()
    {
        var ticket = DomainTestData.Ticket();
        var teamId = Guid.NewGuid();

        ticket.SetClassification(TicketCategory.Database, TicketPriority.Critical, DomainTestData.Now);
        ticket.AssignTo(teamId, DomainTestData.Now);

        Assert.Equal(TicketCategory.Database, ticket.Category);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(teamId, ticket.AssignedTeamId);
        Assert.Equal(3, ticket.Revision);

        ticket.Unassign(DomainTestData.Now);
        Assert.Null(ticket.AssignedTeamId);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void ResolvedOrClosedTicket_RejectsActiveEdits(TicketStatus status)
    {
        var ticket = AtStatus(status);

        Assert.Throws<DomainException>(() => ticket.UpdateDetails("Different title", "Different description", DomainTestData.Now.AddHours(1)));
        Assert.Throws<DomainException>(() => ticket.SetClassification(TicketCategory.Other, TicketPriority.Low, DomainTestData.Now.AddHours(1)));
        Assert.Throws<DomainException>(() => ticket.AssignTo(Guid.NewGuid(), DomainTestData.Now.AddHours(1)));
        Assert.Throws<DomainException>(() => ticket.Unassign(DomainTestData.Now.AddHours(1)));
    }

    [Fact]
    public void AddComment_RejectsAnotherTicketAndDuplicateIdentifiers()
    {
        var ticket = DomainTestData.Ticket();
        var wrong = new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Investigation started.", DomainTestData.Now);
        Assert.Throws<DomainException>(() => ticket.AddComment(wrong));
        Assert.Empty(ticket.Comments);

        var comment = new TicketComment(Guid.NewGuid(), ticket.Id, Guid.NewGuid(), "Investigation started.", DomainTestData.Now);
        ticket.AddComment(comment);
        Assert.Throws<DomainException>(() => ticket.AddComment(comment));
        Assert.Single(ticket.Comments);
        Assert.Equal(2, ticket.Revision);
        Assert.True(((ICollection<TicketComment>)ticket.Comments).IsReadOnly);
    }

    [Fact]
    public void AddComment_CannotBackdateOrModifyAClosedTicket()
    {
        var ticket = AtStatus(TicketStatus.InProgress);
        Assert.Throws<DomainException>(() => ticket.AddComment(new TicketComment(Guid.NewGuid(), ticket.Id,
            Guid.NewGuid(), "Old comment.", DomainTestData.Now)));
        Assert.Empty(ticket.Comments);

        ticket.Resolve("The permissions were corrected.", DomainTestData.Now.AddMinutes(2));
        ticket.Close(DomainTestData.Now.AddMinutes(3));
        Assert.Throws<DomainException>(() => ticket.AddComment(new TicketComment(Guid.NewGuid(), ticket.Id,
            Guid.NewGuid(), "Late comment.", DomainTestData.Now.AddHours(1))));
    }

    [Fact]
    public void InvalidUpdate_DoesNotPartiallyChangeTheTitle()
    {
        var ticket = DomainTestData.Ticket();
        var title = ticket.Title;

        Assert.Throws<DomainException>(() => ticket.UpdateDetails("A new title", " ", DomainTestData.Now));

        Assert.Equal(title, ticket.Title);
        Assert.Equal(1, ticket.Revision);
    }

    [Fact]
    public void EmptyTicketOrRelationshipIdentifiers_AreRejected()
    {
        Assert.Throws<DomainException>(() => new Ticket(Guid.Empty, "IT-1", "Title", "Description", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new Ticket(Guid.NewGuid(), "IT-1", "Title", "Description", Guid.Empty, DomainTestData.Now));
        Assert.Throws<DomainException>(() => DomainTestData.Ticket().AssignTo(Guid.Empty, DomainTestData.Now));
    }

    [Fact]
    public void MandatoryTextAndLengthLimits_AreEnforced()
    {
        Assert.Throws<DomainException>(() => new Ticket(Guid.NewGuid(), " ", "Title", "Description", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new Ticket(Guid.NewGuid(), "IT-1", " ", "Description", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new Ticket(Guid.NewGuid(), "IT-1", "Title", " ", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new Ticket(Guid.NewGuid(), "IT-1", new string('x', 161), "Description", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => DomainTestData.Ticket().UpdateDetails("Title", new string('x', 8001), DomainTestData.Now));
    }

    private static Ticket AtStatus(TicketStatus status)
    {
        var ticket = DomainTestData.Ticket();
        if (status >= TicketStatus.InProgress) ticket.StartWork(DomainTestData.Now.AddMinutes(1));
        if (status >= TicketStatus.Resolved) ticket.Resolve("The permissions were corrected.", DomainTestData.Now.AddMinutes(2));
        if (status >= TicketStatus.Closed) ticket.Close(DomainTestData.Now.AddMinutes(3));
        return ticket;
    }
}
