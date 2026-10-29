using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Attachments;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Tickets;

public sealed class AttachmentPolicyTests
{
    [Theory]
    [InlineData("../log.txt", "text/plain")]
    [InlineData("report.html", "text/html")]
    [InlineData("script.svg", "image/svg+xml")]
    [InlineData("bad.png", "image/png")]
    [InlineData("fake.pdf", "application/pdf")]
    [InlineData("log.txt", "application/pdf")]
    public void RejectsUnsafeNamesTypesAndContent(string name, string type) =>
        Assert.Throws<RequestValidationException>(() => AttachmentPolicy.Validate(name, type, "example"u8.ToArray()));
    [Fact]
    public void EnforcesSizeAndUtf8AndKeepsContentHash()
    {
        Assert.Throws<RequestValidationException>(() => AttachmentPolicy.Validate("log.txt", "text/plain", []));
        Assert.Throws<RequestValidationException>(() => AttachmentPolicy.Validate("log.txt", "text/plain", new byte[AttachmentPolicy.MaximumBytes + 1]));
        Assert.Throws<RequestValidationException>(() => AttachmentPolicy.Validate("log.txt", "text/plain", [255, 0]));
        var valid = AttachmentPolicy.Validate("observations.txt", "text/plain; charset=utf-8", "SQL timeout\nObserved at 10:30"u8.ToArray());
        Assert.Equal(64, valid.Sha256.Length); Assert.Equal("text/plain", valid.ContentType);
    }
    [Fact]
    public void AcceptsSmallPngAndRejectsExcessiveDimensions()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aKxkAAAAASUVORK5CYII=");
        Assert.Equal("image/png", AttachmentPolicy.Validate("screen.png", "image/png", png).ContentType);
        png[16] = 127;
        Assert.Throws<RequestValidationException>(() => AttachmentPolicy.Validate("huge.png", "image/png", png));
    }
}
