using System.Runtime.CompilerServices;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Conversations;
using Xunit;

namespace ITTicketIntelligence.UnitTests;

public sealed class AIChatConversationTests
{
    private sealed class Store : IAIConversationRepository,IUnitOfWork,IAuditLogWriter
    {
        public AIConversation? Value;public int Saves;public List<AuditLog> Audit=[];
        public Task<IReadOnlyList<AIConversation>> ListOwnedChatsAsync(Guid owner,int page,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<AIConversation>>(Value?.UserId==owner && Value.TicketId==null && page==1?[Value]:[]);
        public Task<AIConversation?> GetByIdAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult(Value?.Id==id?Value:null);
        public void Add(AIConversation conversation)=>Value=conversation;
        public void Add(AuditLog audit)=>Audit.Add(audit);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();Saves++;return Task.FromResult(1);}
    }
    private sealed class Chat : IAIChatService
    {
        public bool Fail;public IReadOnlyList<AIChatMessage> Last=[];
        public Task<string> SendAsync(IReadOnlyList<AIChatMessage> messages,string? model,CancellationToken ct)=>throw new NotSupportedException();
        public Task<AIChatHealth> HealthAsync(CancellationToken ct)=>Task.FromResult(new AIChatHealth(true,"fake"));
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AIChatMessage> messages,string? model,[EnumeratorCancellation]CancellationToken ct)
        {Last=messages;await Task.Yield();ct.ThrowIfCancellationRequested();yield return "Hello";if(Fail)throw new LLMServiceException(LLMFailureKind.Unavailable);yield return " there";}
    }
    [Fact]public async Task HistoryAndAuditAreSavedOnlyAfterCompletedAnswer()
    {
        var store=new Store();var chat=new Chat();var service=new AIChatConversations(store,store,chat,store);var owner=Guid.NewGuid();
        var conversation=await service.CreateAsync(owner,"Support",default);
        Assert.Equal("Hello there",await service.SendAsync(conversation.Id,owner,"First question",null,default));
        await service.SendAsync(conversation.Id,owner,"Second question",null,default);
        Assert.Equal(3,chat.Last.Count);Assert.Equal("First question",chat.Last[0].Content);
        Assert.Equal(4,(await service.GetAsync(conversation.Id,owner,default)).Messages.Count);
        Assert.Equal(3,store.Saves);Assert.Equal(3,store.Audit.Count);
        Assert.Single((await service.ListAsync(owner,1,default)).Items);
        Assert.Empty((await service.ListAsync(Guid.NewGuid(),1,default)).Items);
    }
    [Fact]public async Task PartialAnswerNeverBecomesACompletedHistoryEntry()
    {
        var store=new Store();var chat=new Chat{Fail=true};var service=new AIChatConversations(store,store,chat,store);var owner=Guid.NewGuid();
        var conversation=await service.CreateAsync(owner,"Support",default);
        await Assert.ThrowsAsync<LLMServiceException>(()=>service.SendAsync(conversation.Id,owner,"Question",null,default));
        Assert.Empty(store.Value!.Messages);Assert.Equal(1,store.Saves);
    }
    [Fact]public async Task PrivateHistoryRejectsOtherOwnersAndTicketAgentConversations()
    {
        var store=new Store();var service=new AIChatConversations(store,store,new Chat(),store);var owner=Guid.NewGuid();
        var conversation=await service.CreateAsync(owner,"Support",default);
        await Assert.ThrowsAsync<ResourceNotFoundException>(()=>service.GetAsync(conversation.Id,Guid.NewGuid(),default));
        store.Value=new AIConversation(Guid.NewGuid(),owner,DateTimeOffset.UtcNow,Guid.NewGuid(),"Ticket context");
        await Assert.ThrowsAsync<ResourceNotFoundException>(()=>service.GetAsync(store.Value.Id,owner,default));
    }
    [Fact]public async Task OversizedAndCancelledRequestsDoNotAppendHistory()
    {
        var store=new Store();var service=new AIChatConversations(store,store,new Chat(),store);var owner=Guid.NewGuid();
        var conversation=await service.CreateAsync(owner,"Support",default);
        await Assert.ThrowsAsync<RequestValidationException>(()=>service.SendAsync(conversation.Id,owner,new string('x',8001),null,default));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.SendAsync(conversation.Id,owner,"Question",null,cancelled.Token));
        Assert.Empty(store.Value!.Messages);
    }
}
