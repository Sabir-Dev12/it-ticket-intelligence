using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class ValueObjectAndAuditTests
{
    [Fact]
    public void EmailAddress_NormalizesAndComparesByValue()
    {
        var email = EmailAddress.Create(" Sara@Example.COM ");

        Assert.Equal("sara@example.com", email.Value);
        Assert.Equal(EmailAddress.Create("sara@example.com"), email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData("Sara <sara@example.com>")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData(null)]
    public void EmailAddress_RejectsInvalidOrDisplayNameInputs(string? value)
    {
        Assert.Throws<DomainException>(() => EmailAddress.Create(value!));
    }

    [Theory]
    [InlineData(-.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Confidence_RejectsInvalidScores(double value)
    {
        Assert.Throws<DomainException>(() => new PredictionConfidence(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.94)]
    [InlineData(1)]
    public void Confidence_AcceptsFiniteScoresWithinTheInclusiveRange(double value)
    {
        Assert.Equal(value, new PredictionConfidence(value).Value);
        Assert.Equal(new PredictionConfidence(value), new PredictionConfidence(value));
    }

    [Fact]
    public void AuditLog_SupportsUserAndSystemActionsWithOptionalMetadata()
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var human = new AuditLog(Guid.NewGuid(), userId, "TicketAssigned", "Ticket", ticketId,
            DomainTestData.Now, "Assigned by support.", correlationId);
        var system = new AuditLog(Guid.NewGuid(), null, "PredictionRecorded", "Ticket", ticketId, DomainTestData.Now);

        Assert.Equal(userId, human.UserId);
        Assert.Equal(ticketId, human.EntityId);
        Assert.Equal(correlationId, human.CorrelationId);
        Assert.Null(system.UserId);
        Assert.Null(system.Details);
        Assert.Null(system.CorrelationId);
    }

    [Fact]
    public void AuditLog_RejectsMissingActionTargetOrInvalidOptionalIdentifiers()
    {
        Assert.Throws<DomainException>(() => new AuditLog(Guid.NewGuid(), null, " ", "Ticket", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AuditLog(Guid.NewGuid(), null, "Created", " ", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AuditLog(Guid.NewGuid(), null, "Created", "Ticket", Guid.Empty, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AuditLog(Guid.NewGuid(), Guid.Empty, "Created", "Ticket", Guid.NewGuid(), DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AuditLog(Guid.NewGuid(), null, "Created", "Ticket", Guid.NewGuid(), DomainTestData.Now, correlationId: Guid.Empty));
    }

    [Theory]
    [InlineData(typeof(AuditLog))]
    [InlineData(typeof(TicketComment))]
    [InlineData(typeof(TicketPrediction))]
    [InlineData(typeof(AIMessage))]
    public void HistoricalFacts_ExposeNoPropertySetters(Type type)
    {
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ExplicitTimes_AreRequiredAndStoredAsUtc()
    {
        var localTime = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(1));
        var comment = new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Investigation started.", localTime);

        Assert.Equal(TimeSpan.Zero, comment.CreatedAt.Offset);
        Assert.Equal(DomainTestData.Now, comment.CreatedAt);
        Assert.Throws<DomainException>(() => new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Message", default));
    }

    [Fact]
    public void Comment_RequiresValidRelationshipsAndNonemptyBoundedContent()
    {
        Assert.Throws<DomainException>(() => new TicketComment(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Message", DomainTestData.Now));
        Assert.Throws<DomainException>(() => new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Message", DomainTestData.Now));
        Assert.Throws<DomainException>(() => new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", DomainTestData.Now));
        Assert.Throws<DomainException>(() => new TicketComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 4001), DomainTestData.Now));
    }
}
