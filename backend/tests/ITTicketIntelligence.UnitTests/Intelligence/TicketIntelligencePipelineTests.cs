using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Intelligence;

public sealed class TicketIntelligencePipelineTests
{
    [Fact]
    public async Task OrchestratesInOrderUsingPredictedCategoryAndSavesOneCompleteAnalysis()
    {
        var fake = new Dependencies();
        using var cts = new CancellationTokenSource();
        var result = await fake.Pipeline().AnalyzeAsync(fake.Input!.Id, cts.Token);
        Assert.Equal(new[] { "load", "category", "priority", "team", "save" }, fake.Calls);
        Assert.Equal(new IntelligenceResult(TicketCategory.Database, .94, TicketPriority.High, .91, "Database", .88, false), result);
        Assert.All(fake.Tokens, token => Assert.Equal(cts.Token, token));
        var rows = Assert.IsAssignableFrom<IReadOnlyList<TicketPrediction>>(fake.Saved);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows.Select(row => row.AnalysisId).Distinct());
        Assert.All(rows, row => { Assert.NotNull(row.AnalysisId); Assert.Equal(fake.Input.Id, row.TicketId); Assert.Equal(4, row.InputRevision); Assert.Equal(Clock.Now, row.CreatedAt); Assert.False(row.NeedsHumanReview); });
        Assert.Equal("category-test", rows[0].ModelVersion);
        Assert.Equal("priority-test", rows[1].ModelVersion);
        Assert.Equal("team-test", rows[2].ModelVersion);
        Assert.Equal("Database", rows[2].RecommendedTeamName);
        Assert.Null(rows[2].RecommendedTeamId);
    }

    [Theory]
    [InlineData(.79, .91, .88, true, "Database")]
    [InlineData(.94, .79, .88, true, "Database")]
    [InlineData(.94, .91, .79, true, null)]
    [InlineData(.8, .8, .8, false, "Database")]
    public async Task ReviewCombinesAllThreeScoresAndPreservesTeamAbstention(double category, double priority,
        double team, bool review, string? recommendedTeam)
    {
        var fake = new Dependencies { CategoryConfidence = category, PriorityConfidence = priority,
            TeamResult = new TeamRecommendationPolicy().Apply("Database", team, "team-test") };
        var result = await fake.Pipeline().AnalyzeAsync(fake.Input!.Id);
        Assert.Equal(review, result.NeedsHumanReview);
        Assert.Equal(recommendedTeam, result.RecommendedTeam);
        Assert.Equal(team, result.TeamConfidence);
        Assert.Equal(category < .8, fake.Saved![0].NeedsHumanReview);
        Assert.Equal(priority < .8, fake.Saved[1].NeedsHumanReview);
        Assert.Equal(team < .8, fake.Saved[2].NeedsHumanReview);
    }

    [Fact]
    public async Task ReviewThresholdsCanBeReplacedWithoutChangingPredictors()
    {
        var fake = new Dependencies { CategoryConfidence = .85 };
        var result = await fake.Pipeline(new IntelligenceReviewPolicy(.9, .7)).AnalyzeAsync(fake.Input!.Id);
        Assert.True(result.NeedsHumanReview);
        Assert.Equal(.9, fake.Saved![0].ReviewThreshold);
        Assert.Equal(.7, fake.Saved[1].ReviewThreshold);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Closed, false)]
    [InlineData(TicketStatus.Open, true)]
    public async Task InactiveTicketsAreRejectedBeforeInference(TicketStatus status, bool archived)
    {
        var fake = new Dependencies();
        fake.Input = fake.Input! with { Status = status, IsArchived = archived };
        await Assert.ThrowsAsync<ConflictException>(() => fake.Pipeline().AnalyzeAsync(fake.Input.Id));
        Assert.Equal(new[] { "load" }, fake.Calls);
    }

    [Fact]
    public async Task MissingInvalidAndPreCanceledRequestsDoNotRunPredictors()
    {
        var fake = new Dependencies { Input = null };
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => fake.Pipeline().AnalyzeAsync(Guid.NewGuid()));
        Assert.Equal(new[] { "load" }, fake.Calls);
        fake.Calls.Clear();
        await Assert.ThrowsAsync<RequestValidationException>(() => fake.Pipeline().AnalyzeAsync(Guid.Empty));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.Pipeline().AnalyzeAsync(Guid.NewGuid(), cts.Token));
        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("priority")]
    [InlineData("team")]
    public async Task PredictorFailureDoesNotSavePartialResults(string failingStep)
    {
        var fake = new Dependencies { FailAt = failingStep };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.Pipeline().AnalyzeAsync(fake.Input!.Id));
        Assert.DoesNotContain("save", fake.Calls);
        Assert.Null(fake.Saved);
    }

    [Fact]
    public async Task CancellationBetweenStagesPreventsLaterCallsAndPersistence()
    {
        using var cts = new CancellationTokenSource();
        var fake = new Dependencies { AfterPriority = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.Pipeline().AnalyzeAsync(fake.Input!.Id, cts.Token));
        Assert.Equal(new[] { "load", "category", "priority" }, fake.Calls);
    }

    [Fact]
    public async Task PersistenceFailurePreventsReturningASuccessfulAnalysis()
    {
        var fake = new Dependencies { FailAt = "save" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.Pipeline().AnalyzeAsync(fake.Input!.Id));
        Assert.Null(fake.Saved);
    }

    [Theory]
    [InlineData("Unknown", .9, false, .8)]
    [InlineData("Database", .5, true, .8)]
    [InlineData(null, .9, false, .8)]
    [InlineData("Database", .5, false, .8)]
    public async Task InconsistentTeamOutputsAreRejected(string? team, double score, bool review, double threshold)
    {
        var fake = new Dependencies { TeamResult = new(team, score, review, threshold, "test") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.Pipeline().AnalyzeAsync(fake.Input!.Id));
        Assert.DoesNotContain("save", fake.Calls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.1)]
    [InlineData(1.1)]
    public async Task InvalidScoresCannotBePersisted(double score)
    {
        var fake = new Dependencies { CategoryConfidence = score };
        await Assert.ThrowsAsync<DomainException>(() => fake.Pipeline().AnalyzeAsync(fake.Input!.Id));
        Assert.DoesNotContain("save", fake.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReviewPolicyRejectsInvalidThresholds(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntelligenceReviewPolicy(threshold, .8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntelligenceReviewPolicy(.8, threshold));
    }

    [Fact]
    public void DomainRecommendationEnforcesAbstentionAndPreservesAnUnassignedTaxonomyLabel()
    {
        TicketPrediction Create(string? name, double score, double threshold = .8) =>
            TicketPrediction.ForTeamRecommendation(Guid.NewGuid(), Guid.NewGuid(), name,
                new PredictionConfidence(score), "team-test", 1, Clock.Now, Guid.NewGuid(), threshold);
        Assert.Null(Create(null, .7).RecommendedTeamName);
        Assert.Equal("Database", Create("Database", .8).RecommendedTeamName);
        Assert.Throws<DomainException>(() => Create("Database", .7));
        Assert.Throws<DomainException>(() => Create(null, .8));
        Assert.Throws<DomainException>(() => Create(" ", .9));
        Assert.Throws<DomainException>(() => Create("Database", .9, double.NaN));
    }

    private sealed class Clock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Dependencies : ITicketIntelligenceStore, ITicketCategoryPredictor,
        ITicketPriorityPredictor, ITicketTeamPredictor
    {
        public TicketAnalysisInput? Input { get; set; } = new(Guid.NewGuid(), "SQL locks", "Writes are blocked.", 4, TicketStatus.Open, false);
        public double CategoryConfidence { get; init; } = .94;
        public double PriorityConfidence { get; init; } = .91;
        public TeamRecommendation TeamResult { get; init; } = new TeamRecommendationPolicy().Apply("Database", .88, "team-test");
        public string? FailAt { get; init; }
        public Action? AfterPriority { get; init; }
        public List<string> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public IReadOnlyList<TicketPrediction>? Saved { get; private set; }
        public ITicketIntelligencePipeline Pipeline(IntelligenceReviewPolicy? policy = null) =>
            new TicketIntelligencePipeline(this, this, this, this, policy ?? new(), new Clock(),
                new ITTicketIntelligence.Application.Realtime.TicketEventNotifier(new ITTicketIntelligence.Application.Realtime.NullTicketEventSink(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ITTicketIntelligence.Application.Realtime.TicketEventNotifier>.Instance));
        private void Step(string name, CancellationToken token)
        {
            Calls.Add(name); Tokens.Add(token);
            if (FailAt == name) throw new InvalidOperationException("Test failure at " + name);
        }
        public Task<TicketAnalysisInput?> GetInputAsync(Guid id, CancellationToken token = default)
        { Step("load", token); return Task.FromResult(Input); }
        public Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default)
        {
            Step("category", cancellationToken); Assert.Equal(Input!.Title, input.Title); Assert.Equal(Input.Description, input.Description);
            return Task.FromResult(new TicketCategoryPrediction(TicketCategory.Database, CategoryConfidence, "category-test"));
        }
        public Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default)
        {
            Step("priority", cancellationToken); Assert.Equal(TicketCategory.Database, input.Category);
            Assert.Equal(Input!.Title, input.Title); Assert.Equal(Input.Description, input.Description);
            AfterPriority?.Invoke();
            return Task.FromResult(new TicketPriorityPrediction(TicketPriority.High, PriorityConfidence, "priority-test"));
        }
        public Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default)
        { Step("team", cancellationToken); Assert.Equal(Input!.Title, input.Title); Assert.Equal(Input.Description, input.Description); return Task.FromResult(TeamResult); }
        public Task SaveAsync(Guid id, int revision, IReadOnlyList<TicketPrediction> predictions, CancellationToken token = default)
        { Step("save", token); Assert.Equal(Input!.Id, id); Assert.Equal(Input.Revision, revision); Saved = predictions; return Task.CompletedTask; }
    }
}
