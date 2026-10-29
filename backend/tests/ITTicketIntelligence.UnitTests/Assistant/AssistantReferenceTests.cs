using ITTicketIntelligence.Application.Assistant;
using ITTicketIntelligence.Application.Common;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Assistant;
public sealed class AssistantReferenceTests
{
    [Theory]
    [InlineData("Why is ticket #1045 failing?", "IT-1045")]
    [InlineData("Explain IT-1045", "IT-1045")]
    [InlineData("Why is #1045 failing?", "IT-1045")]
    [InlineData("ticket 001045", "IT-1045")]
    [InlineData("What should I inspect next?", null)]
    public void ResolvesOnlyExplicitTicketReferences(string question, string? expected) => Assert.Equal(expected, AssistantService.ParseReference(question));
    [Theory]
    [InlineData("Compare ticket #1045 and IT-1046")]
    [InlineData("Compare ticket #1045 and #1046")]
    [InlineData("ticket #000")]
    public void RejectsAmbiguousOrEmptyReferences(string question) => Assert.Throws<RequestValidationException>(() => AssistantService.ParseReference(question));
}
