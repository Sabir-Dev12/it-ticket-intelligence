using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Domain.Enums;
using Xunit;

namespace ITTicketIntelligence.UnitTests.LanguageModels;

public sealed class LLMContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\0prompt")]
    public void PromptIsRequiredAndBounded(string? prompt)
    {
        Assert.Throws<RequestValidationException>(() => new LLMRequest(prompt!));
        Assert.Throws<RequestValidationException>(() => new LLMRequest(new string('x', 8001)));
    }

    [Fact]
    public void RagContextIsSnapshottedAndHasSizeAndProvenanceLimits()
    {
        var source = new List<RetrievedChunk> { Chunk() };
        var request = new LLMRequest(" Investigate SQL timeout ", new("space", source)); source.Clear();
        Assert.Equal("Investigate SQL timeout", request.Prompt); Assert.Single(request.Context);
        Assert.True(((ICollection<RetrievedChunk>)request.Context).IsReadOnly);
        var duplicate = Chunk();
        Assert.Throws<RequestValidationException>(() => new LLMRequest("Prompt", new("space", [duplicate, duplicate])));
        Assert.Throws<RequestValidationException>(() => new LLMRequest("Prompt", new("space", Enumerable.Range(0,21).Select(_ => Chunk()).ToArray())));
        Assert.Throws<RequestValidationException>(() => new LLMRequest("Prompt", new("space", [Chunk() with { DocumentId = Guid.Empty }])));
        Assert.Throws<RequestValidationException>(() => new LLMRequest("Prompt", new("space", Enumerable.Range(0,10).Select(_ => Chunk() with { Chunk = new string('x',3000) }).ToArray())));
    }

    [Fact]
    public async Task ApplicationCanUseAFakeWithoutInfrastructureOrDatabase()
    {
        var fake = new FakeLLMService(); ILLMService service = fake;
        using var cts = new CancellationTokenSource();
        var request = new LLMRequest("Investigate SQL timeout", new("space", [Chunk()]));
        var result = await service.GenerateAsync(request, cts.Token);
        Assert.Same(request, fake.Request); Assert.Equal(cts.Token, fake.Token);
        Assert.Equal("Check SQL connectivity.", result.Answer.Summary); Assert.True(result.NeedsHumanReview);
        Assert.Equal(request.Context[0].ChunkId, Assert.Single(result.Citations).ChunkId);
    }

    [Fact]
    public async Task FakeHonorsCallerCancellation()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FakeLLMService().GenerateAsync(new("Prompt"), cts.Token));
    }

    private static RetrievedChunk Chunk()
    {
        var id = Guid.NewGuid();
        return new(id, "SQL guide", Guid.NewGuid(), "Check TCP port 1433.", .9,
            new KnowledgeChunkMetadata(id, 2, "SQL guide", "kb/sql", TicketCategory.Database,
                KnowledgeDocumentKind.Procedure, KnowledgeContentFormat.PlainText, 0, 0, 20, new string('a',64)));
    }
    private sealed class FakeLLMService : ILLMService
    {
        public LLMRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Request = request; Token = cancellationToken;
            return Task.FromResult(new LLMResponse(new("Check SQL connectivity.", ["Verify port 1433."], false),
                request.Context.Select(c => new LLMCitation(c.DocumentId,c.ChunkId,c.Metadata.DocumentVersion,c.Title,c.Metadata.Source,c.Metadata.ContentHash)).ToArray(),
                "Fake", "test-only", 1, true));
        }
    }
}
