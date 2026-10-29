using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class ConversationTests
{
    [Fact]
    public void Conversation_CanBeGeneralOrLinkedToATicket()
    {
        var general = NewConversation();
        var ticketId = Guid.NewGuid();
        var linked = new AIConversation(Guid.NewGuid(), Guid.NewGuid(), DomainTestData.Now, ticketId);

        Assert.Null(general.TicketId);
        Assert.Null(general.Title);
        Assert.Null(general.LastMessageAt);
        Assert.Equal(ticketId, linked.TicketId);
        Assert.Throws<DomainException>(() => new AIConversation(Guid.NewGuid(), Guid.Empty, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AIConversation(Guid.NewGuid(), Guid.NewGuid(), DomainTestData.Now, Guid.Empty));
    }

    [Fact]
    public void AddingMessages_PreservesOrderAndUpdatesConversationTime()
    {
        var conversation = NewConversation();
        var user = Message(conversation.Id, AIMessageRole.User, DomainTestData.Now.AddMinutes(1));
        var assistant = Message(conversation.Id, AIMessageRole.Assistant, DomainTestData.Now.AddMinutes(2));

        conversation.AddMessage(user);
        conversation.AddMessage(assistant);

        Assert.Equal(new[] { user, assistant }, conversation.Messages);
        Assert.Equal(assistant.CreatedAt, conversation.LastMessageAt);
        Assert.Equal(assistant.CreatedAt, conversation.UpdatedAt);
        Assert.True(((ICollection<AIMessage>)conversation.Messages).IsReadOnly);
    }

    [Fact]
    public void Message_CannotBeAddedToAnotherConversationOrAddedTwice()
    {
        var conversation = NewConversation();
        Assert.Throws<DomainException>(() => conversation.AddMessage(Message(Guid.NewGuid(), AIMessageRole.User, DomainTestData.Now)));
        var message = Message(conversation.Id, AIMessageRole.User, DomainTestData.Now);
        conversation.AddMessage(message);

        Assert.Throws<DomainException>(() => conversation.AddMessage(message));
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void Conversation_RejectsMessagesPredatingItsCreationOrLastChange()
    {
        var conversation = NewConversation();
        Assert.Throws<DomainException>(() => conversation.AddMessage(Message(conversation.Id, AIMessageRole.User, DomainTestData.Now.AddMinutes(-1))));

        conversation.AddMessage(Message(conversation.Id, AIMessageRole.User, DomainTestData.Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => conversation.AddMessage(Message(conversation.Id, AIMessageRole.Assistant, DomainTestData.Now.AddMinutes(1))));
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void Rename_ValidatesBeforeMutatingTheConversation()
    {
        var conversation = NewConversation();
        conversation.Rename(" SQL investigation ", DomainTestData.Now.AddMinutes(1));
        Assert.Equal("SQL investigation", conversation.Title);
        Assert.Throws<DomainException>(() => conversation.Rename("Different title", DomainTestData.Now));
        Assert.Equal("SQL investigation", conversation.Title);

        conversation.Rename(null, DomainTestData.Now.AddMinutes(2));
        Assert.Null(conversation.Title);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("check_database_status", null)]
    [InlineData(null, "call-1")]
    public void ToolMessage_RequiresToolNameAndCallIdentifier(string? toolName, string? toolCallId)
    {
        Assert.Throws<DomainException>(() => new AIMessage(Guid.NewGuid(), Guid.NewGuid(), AIMessageRole.Tool,
            "Database is available.", DomainTestData.Now, toolName, toolCallId));
    }

    [Fact]
    public void ToolMetadata_IsReservedForToolResults()
    {
        var message = new AIMessage(Guid.NewGuid(), Guid.NewGuid(), AIMessageRole.Tool, "Database is available.",
            DomainTestData.Now, "check_database_status", "call-1");

        Assert.Equal("check_database_status", message.ToolName);
        Assert.Equal("call-1", message.ToolCallId);
        Assert.Throws<DomainException>(() => new AIMessage(Guid.NewGuid(), Guid.NewGuid(), AIMessageRole.User,
            "Question", DomainTestData.Now, "check_database_status", "call-1"));
    }

    [Fact]
    public void Message_RequiresValidRelationshipRoleAndContent()
    {
        Assert.Throws<DomainException>(() => Message(Guid.Empty, AIMessageRole.User, DomainTestData.Now));
        Assert.Throws<DomainException>(() => Message(Guid.NewGuid(), (AIMessageRole)999, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AIMessage(Guid.NewGuid(), Guid.NewGuid(), AIMessageRole.User, " ", DomainTestData.Now));
        Assert.Throws<DomainException>(() => new AIMessage(Guid.NewGuid(), Guid.NewGuid(), AIMessageRole.User, new string('x', 16001), DomainTestData.Now));
    }

    private static AIConversation NewConversation() => new(Guid.NewGuid(), Guid.NewGuid(), DomainTestData.Now);
    private static AIMessage Message(Guid conversationId, AIMessageRole role, DateTimeOffset createdAt) => new(Guid.NewGuid(), conversationId, role, "Investigate the connection error.", createdAt);
}
