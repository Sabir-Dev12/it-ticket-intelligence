using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class PredictionTests
{
    [Fact]
    public void Factories_OnlyPopulateTheResultForTheirPredictionType()
    {
        var ticketId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var confidence = new PredictionConfidence(.91);
        var category = TicketPrediction.ForCategory(Guid.NewGuid(), ticketId, TicketCategory.Database,
            confidence, "category-v1", 1, DomainTestData.Now);
        var priority = TicketPrediction.ForPriority(Guid.NewGuid(), ticketId, TicketPriority.High,
            confidence, "priority-v1", 1, DomainTestData.Now);
        var routing = TicketPrediction.ForTeamRouting(Guid.NewGuid(), ticketId, teamId,
            confidence, "routing-v1", 1, DomainTestData.Now);

        Assert.Equal(PredictionType.Category, category.Type);
        Assert.Equal(TicketCategory.Database, category.PredictedCategory);
        Assert.Null(category.PredictedPriority);
        Assert.Null(category.RecommendedTeamId);
        Assert.Equal(PredictionType.Priority, priority.Type);
        Assert.Equal(TicketPriority.High, priority.PredictedPriority);
        Assert.Null(priority.PredictedCategory);
        Assert.Null(priority.RecommendedTeamId);
        Assert.Equal(PredictionType.TeamRouting, routing.Type);
        Assert.Equal(teamId, routing.RecommendedTeamId);
        Assert.Null(routing.PredictedCategory);
        Assert.Null(routing.PredictedPriority);
    }

    [Fact]
    public void RecordingPredictions_DoesNotApplyTheirRecommendations()
    {
        var ticket = DomainTestData.Ticket();
        var confidence = new PredictionConfidence(.91);

        ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database,
            confidence, "v1", 1, DomainTestData.Now));
        ticket.RecordPrediction(TicketPrediction.ForPriority(Guid.NewGuid(), ticket.Id, TicketPriority.High,
            confidence, "v1", 1, DomainTestData.Now));
        ticket.RecordPrediction(TicketPrediction.ForTeamRouting(Guid.NewGuid(), ticket.Id, Guid.NewGuid(),
            confidence, "v1", 1, DomainTestData.Now));

        Assert.Equal(3, ticket.Predictions.Count);
        Assert.Equal(1, ticket.Revision);
        Assert.Null(ticket.Category);
        Assert.Null(ticket.Priority);
        Assert.Null(ticket.AssignedTeamId);
    }

    [Fact]
    public void HistoricalPrediction_IsRetainedWithoutOverwritingAnExplicitClassification()
    {
        var ticket = DomainTestData.Ticket();
        ticket.SetClassification(TicketCategory.Network, TicketPriority.Critical, DomainTestData.Now.AddMinutes(1));

        ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database,
            new PredictionConfidence(.91), "v1", 1, DomainTestData.Now.AddMinutes(2)));

        Assert.Single(ticket.Predictions);
        Assert.Equal(TicketCategory.Network, ticket.Category);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
    }

    [Fact]
    public void Predictions_RejectForeignTicketsDuplicatesFutureRevisionsAndBackdatedResults()
    {
        var ticket = DomainTestData.Ticket();
        var confidence = new PredictionConfidence(.8);
        var valid = TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database,
            confidence, "v1", 1, DomainTestData.Now);
        ticket.RecordPrediction(valid);

        Assert.Throws<DomainException>(() => ticket.RecordPrediction(valid));
        Assert.Throws<DomainException>(() => ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), Guid.NewGuid(),
            TicketCategory.Database, confidence, "v1", 1, DomainTestData.Now)));
        Assert.Throws<DomainException>(() => ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id,
            TicketCategory.Database, confidence, "v1", 2, DomainTestData.Now)));
        Assert.Throws<DomainException>(() => ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id,
            TicketCategory.Database, confidence, "v1", 1, DomainTestData.Now.AddSeconds(-1))));
        Assert.Single(ticket.Predictions);
        Assert.True(((ICollection<TicketPrediction>)ticket.Predictions).IsReadOnly);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Prediction_RequiresAPositiveInputRevision(int revision)
    {
        Assert.Throws<DomainException>(() => TicketPrediction.ForCategory(Guid.NewGuid(), Guid.NewGuid(), TicketCategory.Other,
            new PredictionConfidence(.5), "v1", revision, DomainTestData.Now));
    }

    [Fact]
    public void Prediction_RequiresValidResultAndModelMetadata()
    {
        var score = new PredictionConfidence(.8);
        Assert.Throws<DomainException>(() => TicketPrediction.ForCategory(Guid.NewGuid(), Guid.NewGuid(), (TicketCategory)999,
            score, "v1", 1, DomainTestData.Now));
        Assert.Throws<DomainException>(() => TicketPrediction.ForPriority(Guid.NewGuid(), Guid.NewGuid(), (TicketPriority)999,
            score, "v1", 1, DomainTestData.Now));
        Assert.Throws<DomainException>(() => TicketPrediction.ForTeamRouting(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
            score, "v1", 1, DomainTestData.Now));
        Assert.Throws<DomainException>(() => TicketPrediction.ForCategory(Guid.NewGuid(), Guid.NewGuid(), TicketCategory.Other,
            score, " ", 1, DomainTestData.Now));
    }
}
