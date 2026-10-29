using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Intelligence;

public sealed class TicketPriorityTests
{
    [Theory]
    [InlineData("", "Description")]
    [InlineData(" ", "Description")]
    [InlineData("Title", "")]
    [InlineData("Title", " ")]
    public void InputRejectsBlankText(string title, string description) =>
        Assert.Throws<RequestValidationException>(() => new TicketPriorityInput(title, description, TicketCategory.Network));

    [Fact]
    public void InputValidatesCategoryAndTextBounds()
    {
        Assert.Throws<RequestValidationException>(() => new TicketPriorityInput("Title", "Description", (TicketCategory)999));
        Assert.Throws<RequestValidationException>(() => new TicketPriorityInput(new string('x', 161), "Description", TicketCategory.Account));
        Assert.Throws<RequestValidationException>(() => new TicketPriorityInput("Title", new string('x', 8001), TicketCategory.Account));
        var input = new TicketPriorityInput(new string('x', 160), new string('x', 8000), TicketCategory.Other);
        Assert.Equal(TicketCategory.Other, input.Category);
    }

    [Fact]
    public async Task HandlerPassesOnlyCreationInputThroughThePort()
    {
        var predictor = new FakePredictor();
        using var cancellation = new CancellationTokenSource();
        var result = await new PredictTicketPriorityHandler(predictor).HandleAsync(new PredictTicketPriorityQuery(" Service down ", " All sites are affected. ", TicketCategory.Network), cancellation.Token);
        Assert.Equal(predictor.Result, result);
        Assert.NotNull(predictor.Input);
        Assert.Equal("Service down", predictor.Input.Title);
        Assert.Equal("All sites are affected.", predictor.Input.Description);
        Assert.Equal(TicketCategory.Network, predictor.Input.Category);
        Assert.Equal(cancellation.Token, predictor.Token);
        Assert.Equal(1, predictor.Calls);
    }

    [Fact]
    public async Task InvalidCategoryDoesNotReachPredictor()
    {
        var predictor = new FakePredictor();
        await Assert.ThrowsAsync<RequestValidationException>(() => new PredictTicketPriorityHandler(predictor).HandleAsync(new PredictTicketPriorityQuery("Title", "Description", (TicketCategory)999)));
        Assert.Equal(0, predictor.Calls);
    }

    [Fact]
    public async Task CancellationDoesNotReachPredictor()
    {
        var predictor = new FakePredictor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PredictTicketPriorityHandler(predictor).HandleAsync(new PredictTicketPriorityQuery("Title", "Description", TicketCategory.Network), cancellation.Token));
        Assert.Equal(0, predictor.Calls);
    }

    private sealed class FakePredictor : ITicketPriorityPredictor
    {
        public TicketPriorityPrediction Result { get; } = new(TicketPriority.High, 0.75, "fake-priority");
        public TicketPriorityInput? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<TicketPriorityPrediction> PredictAsync(TicketPriorityInput input, CancellationToken cancellationToken = default)
        {
            Input = input; Token = cancellationToken; Calls++;
            return Task.FromResult(Result);
        }
    }
}
