using System.Runtime.CompilerServices;
using System.Text;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.LanguageModels;

public sealed record AIChatSnapshot(Guid Id,string? Title,IReadOnlyList<AIChatMessage> Messages);
public sealed record AIChatEvent(string Type,string? Text=null,Guid? ConversationId=null);
public sealed record AIChatSummary(Guid Id,string? Title,DateTimeOffset UpdatedAt);
public sealed record AIChatPage(IReadOnlyList<AIChatSummary> Items,int Page,bool HasMore);

public sealed class AIChatConversations(IAIConversationRepository conversations,IUnitOfWork work,IAIChatService chat,IAuditLogWriter audit)
{
    public async Task<AIChatPage> ListAsync(Guid owner,int page,CancellationToken ct)
    {
        if(page is <1 or >10000)throw new RequestValidationException(new Dictionary<string,string[]>{["page"]=["Use a page between 1 and 10000."]});
        var items=await conversations.ListOwnedChatsAsync(owner,page,ct);
        return new(items.Take(20).Select(c=>new AIChatSummary(c.Id,c.Title,c.UpdatedAt)).ToArray(),page,items.Count>20);
    }
    public async Task<AIChatSnapshot> CreateAsync(Guid owner,string title,CancellationToken ct)
    {
        var entity=new AIConversation(Guid.NewGuid(),owner,DateTimeOffset.UtcNow,title:title);
        conversations.Add(entity);
        audit.Add(new Domain.Auditing.AuditLog(Guid.NewGuid(),owner,"AIChat.Created","AIConversation",entity.Id,DateTimeOffset.UtcNow));
        await work.SaveChangesAsync(ct);
        return new(entity.Id,entity.Title,[]);
    }
    private async Task<AIConversation> OwnedAsync(Guid id,Guid owner,CancellationToken ct)
    {
        var entity=await conversations.GetByIdAsync(id,ct);
        if(entity is null || entity.UserId!=owner || entity.TicketId.HasValue) throw new ResourceNotFoundException("Conversation");
        return entity;
    }
    public async Task<AIChatSnapshot> GetAsync(Guid id,Guid owner,CancellationToken ct)
    {
        var entity=await OwnedAsync(id,owner,ct);
        return new(entity.Id,entity.Title,entity.Messages.Select(m=>new AIChatMessage(m.Role==AIMessageRole.User?"user":"assistant",m.Content)).ToArray());
    }
    public async Task<string> SendAsync(Guid id,Guid owner,string message,string? model,CancellationToken ct)
    {
        var answer=new StringBuilder();
        await foreach(var item in StreamAsync(id,owner,message,model,ct)) if(item.Type=="delta")answer.Append(item.Text);
        return answer.ToString();
    }
    public async IAsyncEnumerable<AIChatEvent> StreamAsync(Guid id,Guid owner,string message,string? model,[EnumeratorCancellation] CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(message) || message.Length>8000 || message.Contains('\0'))
            throw new RequestValidationException(new Dictionary<string,string[]> { ["message"]=["Use 1 to 8000 characters without NUL."] });
        var entity=await OwnedAsync(id,owner,ct);
        if(entity.Messages.Count>=100)throw new ConflictException("Start a new conversation after 50 exchanges.");
        var history=entity.Messages.TakeLast(20).Select(m=>new AIChatMessage(m.Role==AIMessageRole.User?"user":"assistant",m.Content)).ToList();
        while(history.Sum(m=>m.Content.Length)+message.Length>32000 && history.Count>0)history.RemoveAt(0);
        history.Add(new("user",message));
        var answer=new StringBuilder();
        await foreach(var text in chat.StreamAsync(history,model,ct)) { answer.Append(text);yield return new("delta",text); }
        ct.ThrowIfCancellationRequested();
        var now=DateTimeOffset.UtcNow;
        entity.AddMessage(new AIMessage(Guid.NewGuid(),id,AIMessageRole.User,message,now));
        entity.AddMessage(new AIMessage(Guid.NewGuid(),id,AIMessageRole.Assistant,answer.ToString(),now));
        audit.Add(new Domain.Auditing.AuditLog(Guid.NewGuid(),owner,"AIChat.Completed","AIConversation",id,now));
        await work.SaveChangesAsync(ct);
        yield return new("done",ConversationId:id);
    }
}
