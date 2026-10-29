using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Intelligence;

public sealed class TicketTeamTests
{
    [Theory]
    [InlineData("", "Description")]
    [InlineData(" ", "Description")]
    [InlineData("Title", "")]
    [InlineData("Title", " ")]
    public void InputRejectsBlankText(string title, string description) => Assert.Throws<RequestValidationException>(() => new TicketTeamInput(title, description));

    [Fact]
    public void InputEnforcesTicketBounds()
    {
        Assert.Throws<RequestValidationException>(() => new TicketTeamInput(new string('x', 161), "Description"));
        Assert.Throws<RequestValidationException>(() => new TicketTeamInput("Title", new string('x', 8001)));
        Assert.Equal(160, new TicketTeamInput(new string('x', 160), new string('x', 8000)).Title.Length);
    }

    [Theory]
    [InlineData(0.799, true)]
    [InlineData(0.8, false)]
    [InlineData(0.801, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void PolicyActuallyWithholdsTheTeamBelowThreshold(double confidence, bool review)
    {
        var result = new TeamRecommendationPolicy(0.8).Apply("Database", confidence, "team-test");
        Assert.Equal(review, result.NeedsHumanReview);
        Assert.Equal(review ? null : "Database", result.Team);
        Assert.Equal(confidence, result.Confidence);
        Assert.Equal(0.8, result.Threshold);
        Assert.Equal("team-test", result.ModelVersion);
    }

    [Fact]
    public void PolicySupportsDifferentServerThresholds()
    {
        Assert.NotNull(new TeamRecommendationPolicy(0.5).Apply("Support", 0.6, "test").Team);
        Assert.Null(new TeamRecommendationPolicy(0.9).Apply("Support", 0.6, "test").Team);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidThresholdIsRejected(double threshold) => Assert.Throws<ArgumentOutOfRangeException>(() => new TeamRecommendationPolicy(threshold));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidConfidenceIsRejected(double confidence) => Assert.Throws<ArgumentOutOfRangeException>(() => new TeamRecommendationPolicy().Apply("Support", confidence, "test"));

    [Fact]
    public void UnknownTeamsAndMissingModelVersionAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TeamRecommendationPolicy().Apply("Unknown", 0.9, "test"));
        Assert.Throws<ArgumentException>(() => new TeamRecommendationPolicy().Apply("Support", 0.9, " "));
    }

    [Fact]
    public async Task HandlerDelegatesOnlyInitialTextAndPropagatesCancellation()
    {
        var predictor = new FakePredictor();
        using var cancellation = new CancellationTokenSource();
        var result = await new PredictTicketTeamHandler(predictor).HandleAsync(new PredictTicketTeamQuery(" SQL locks ", " Writes are blocked. "), cancellation.Token);
        Assert.Equal(predictor.Result, result);
        Assert.NotNull(predictor.Input);
        Assert.Equal("SQL locks", predictor.Input.Title);
        Assert.Equal("Writes are blocked.", predictor.Input.Description);
        Assert.Equal(cancellation.Token, predictor.Token);
        Assert.Equal(1, predictor.Calls);
    }

    [Fact]
    public async Task InvalidOrCanceledRequestsDoNotCallThePredictor()
    {
        var predictor = new FakePredictor();
        var handler = new PredictTicketTeamHandler(predictor);
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(new PredictTicketTeamQuery("", "Description")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new PredictTicketTeamQuery("Title", "Description"), cancellation.Token));
        Assert.Equal(0, predictor.Calls);
    }

    private sealed class FakePredictor : ITicketTeamPredictor
    {
        public TeamRecommendation Result { get; } = new TeamRecommendationPolicy().Apply("Database", 0.6, "fake-team");
        public TicketTeamInput? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default)
        {
            Input = input; Token = cancellationToken; Calls++;
            return Task.FromResult(Result);
        }
    }
}
