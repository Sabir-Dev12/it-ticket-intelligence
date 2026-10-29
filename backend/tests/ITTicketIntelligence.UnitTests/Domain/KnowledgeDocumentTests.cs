using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Knowledge;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class KnowledgeDocumentTests
{
    [Fact]
    public void Update_VersionsTheDocumentAndPreservesItsIdentityAndAuthor()
    {
        var author = Guid.NewGuid();
        var document = NewDocument(author);
        var id = document.Id;

        document.Update("SQL timeout procedure", "Verify the query timeout and service health.",
            "knowledge-base/sql-server/timeout.md", TicketCategory.Database, DomainTestData.Now.AddHours(1));

        Assert.Equal(id, document.Id);
        Assert.Equal(author, document.CreatedById);
        Assert.Equal(2, document.Version);
        Assert.Equal(DomainTestData.Now, document.CreatedAt);
        Assert.Equal(DomainTestData.Now.AddHours(1), document.UpdatedAt);
    }

    [Fact]
    public void InvalidUpdate_DoesNotChangeTheDocumentOrItsVersion()
    {
        var document = NewDocument();

        Assert.Throws<DomainException>(() => document.Update("Different title", "Different content", " ", TicketCategory.Database, DomainTestData.Now));
        Assert.Throws<DomainException>(() => document.Update("Different title", "Different content", "file.md", (TicketCategory)999, DomainTestData.Now));
        Assert.Throws<DomainException>(() => document.Update("Different title", "Different content", "file.md", TicketCategory.Database, DomainTestData.Now.AddMinutes(-1)));

        Assert.Equal("Connection errors", document.Title);
        Assert.Equal(1, document.Version);
    }

    [Fact]
    public void Document_RequiresContentAndSource_AndDistinguishesAbsentAuthorFromInvalidAuthor()
    {
        Assert.Null(NewDocument().CreatedById);
        Assert.Throws<DomainException>(() => NewDocument(Guid.Empty));
        Assert.Throws<DomainException>(() => new KnowledgeDocument(Guid.NewGuid(), "Title", " ", "file.md", TicketCategory.Other, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new KnowledgeDocument(Guid.NewGuid(), "Title", "Content", " ", TicketCategory.Other, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new KnowledgeDocument(Guid.NewGuid(), "Title", new string('x', 100001), "file.md", TicketCategory.Other, DomainTestData.Now));
    }

    private static KnowledgeDocument NewDocument(Guid? author = null) => new(Guid.NewGuid(), "Connection errors",
        "Verify the database service and its configured endpoint.", "knowledge-base/sql-server/connection-errors.md",
        TicketCategory.Database, DomainTestData.Now, author);
}
