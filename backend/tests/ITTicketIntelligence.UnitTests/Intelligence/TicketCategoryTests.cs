using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Intelligence;

public sealed class TicketCategoryTests
{
    [Theory]
    [InlineData("", "A description")]
    [InlineData(" ", "A description")]
    [InlineData("Title", "")]
    [InlineData("Title", " ")]
    public void InputRejectsBlankText(string title, string description) =>
        Assert.Throws<RequestValidationException>(() => new TicketCategoryInput(title, description));

    [Fact]
    public void InputEnforcesTicketLengthLimits()
    {
        Assert.Throws<RequestValidationException>(() => new TicketCategoryInput(new string('x', 161), "Description"));
        Assert.Throws<RequestValidationException>(() => new TicketCategoryInput("Title", new string('x', 8001)));
        var input = new TicketCategoryInput(new string('x', 160), new string('x', 8000));
        Assert.Equal(160, input.Title.Length);
        Assert.Equal(8000, input.Description.Length);
    }

    [Fact]
    public async Task HandlerDelegatesThroughPortAndPropagatesCancellationToken()
    {
        var predictor = new FakePredictor();
        var handler = new PredictTicketCategoryHandler(predictor);
        using var cancellation = new CancellationTokenSource();
        var result = await handler.HandleAsync(new PredictTicketCategoryQuery(" Invoice issue ", " Cannot open the invoice. "), cancellation.Token);
        Assert.Equal(predictor.Result, result);
        Assert.NotNull(predictor.Input);
        Assert.Equal("Invoice issue", predictor.Input.Title);
        Assert.Equal("Cannot open the invoice.", predictor.Input.Description);
        Assert.Equal(cancellation.Token, predictor.Token);
        Assert.Equal(1, predictor.Calls);
    }

    [Fact]
    public async Task InvalidInputDoesNotCallPredictor()
    {
        var predictor = new FakePredictor();
        await Assert.ThrowsAsync<RequestValidationException>(() => new PredictTicketCategoryHandler(predictor).HandleAsync(new PredictTicketCategoryQuery("", "Description")));
        Assert.Equal(0, predictor.Calls);
    }

    [Fact]
    public async Task CanceledRequestDoesNotCallPredictor()
    {
        var predictor = new FakePredictor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PredictTicketCategoryHandler(predictor).HandleAsync(new PredictTicketCategoryQuery("Title", "Description"), cancellation.Token));
        Assert.Equal(0, predictor.Calls);
    }

    private sealed class FakePredictor : ITicketCategoryPredictor
    {
        public TicketCategoryPrediction Result { get; } = new(TicketCategory.Billing, 0.75, "fake-v1");
        public TicketCategoryInput? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<TicketCategoryPrediction> PredictAsync(TicketCategoryInput input, CancellationToken cancellationToken = default)
        {
            Input = input;
            Token = cancellationToken;
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
