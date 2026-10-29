using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Domain.Conversations;

public sealed class AIMessage : Entity
{
    public AIMessage(Guid id, Guid conversationId, AIMessageRole role, string content, DateTimeOffset createdAt,
        string? toolName = null, string? toolCallId = null) : base(id)
    {
        ConversationId = Guard.Id(conversationId, nameof(conversationId));
        Role = Guard.Defined(role, nameof(role));
        Content = Guard.RequiredText(content, nameof(content), 16000);
        ToolName = Guard.OptionalText(toolName, nameof(toolName), 128);
        ToolCallId = Guard.OptionalText(toolCallId, nameof(toolCallId), 128);
        if (role == AIMessageRole.Tool && (ToolName is null || ToolCallId is null))
        {
            throw new DomainException("A tool message requires its tool name and call identifier.");
        }

        if (role != AIMessageRole.Tool && (ToolName is not null || ToolCallId is not null))
        {
            throw new DomainException("Only tool messages can contain tool result metadata.");
        }

        CreatedAt = Guard.Timestamp(createdAt, nameof(createdAt));
    }

    public Guid ConversationId { get; }
    public AIMessageRole Role { get; }
    public string Content { get; }
    public string? ToolName { get; }
    public string? ToolCallId { get; }
    public DateTimeOffset CreatedAt { get; }
}
