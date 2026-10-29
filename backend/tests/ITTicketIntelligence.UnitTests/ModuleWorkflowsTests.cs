using ITTicketIntelligence.Application.Assistant;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Tickets.Commands;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace ITTicketIntelligence.UnitTests;
public sealed class ModuleWorkflowsTests
{
    private sealed class ScopedKnowledge:ITTicketIntelligence.Application.Abstractions.Persistence.IKnowledgeRepository,IKnowledgeAccessContext
    {
        public bool CanSearch=>true;public bool IsAdmin{get;set;}public Guid? UserId=>null;
        public ITTicketIntelligence.Domain.Knowledge.KnowledgeDocument Document=new(Guid.NewGuid(),"Restricted","Private evidence","kb/private",TicketCategory.Database,DateTimeOffset.UtcNow);
        public Task<ITTicketIntelligence.Domain.Knowledge.KnowledgeDocument?> GetByIdAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult<ITTicketIntelligence.Domain.Knowledge.KnowledgeDocument?>(Document);
        public Task<KnowledgePage> GetPageAsync(GetKnowledgePageQuery query,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<bool?> IsAuthorActiveAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult<bool?>(true);
        public void Add(ITTicketIntelligence.Domain.Knowledge.KnowledgeDocument document)=>throw new NotSupportedException();
        public void Remove(ITTicketIntelligence.Domain.Knowledge.KnowledgeDocument document)=>throw new NotSupportedException();
    }
    [Fact]public async Task RestrictedArticleWithoutOwnerIsNotPublicWhenCallerHasNoUserId()
    {var ports=new ScopedKnowledge();ports.Document.SetRestricted(true,DateTimeOffset.UtcNow);var reader=new KnowledgeQueryHandler(ports,ports);await Assert.ThrowsAsync<ResourceNotFoundException>(()=>reader.HandleAsync(new GetKnowledgeQuery(ports.Document.Id)));ports.IsAdmin=true;Assert.Equal(ports.Document.Id,(await reader.HandleAsync(new GetKnowledgeQuery(ports.Document.Id))).Id);}
    private sealed class TicketPorts:ICommandHandler<CreateTicketCommand,TicketDto>,IQueryHandler<GetTicketQuery,TicketDto>,ITicketIntelligencePipeline
    {
        public List<string> Calls=[];public bool Fail;
        public TicketDto Ticket=new(Guid.NewGuid(),"IT-1045","SQL timeout","Cannot connect",Guid.NewGuid(),TicketStatus.Open,null,null,null,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,null,null,false,null,[],[]);
        public Task<TicketDto> HandleAsync(CreateTicketCommand command,CancellationToken cancellationToken=default){Calls.Add("save");return Task.FromResult(Ticket);}
        public Task<TicketDto> HandleAsync(GetTicketQuery query,CancellationToken cancellationToken=default){Calls.Add("read");return Task.FromResult(Ticket);}
        public Task<IntelligenceResult> AnalyzeAsync(Guid id,CancellationToken cancellationToken=default){Calls.Add("predict");if(Fail)throw new InvalidOperationException();return Task.FromResult(new IntelligenceResult(TicketCategory.Database,.9,TicketPriority.High,.9,null,.2,true));}
    }
    [Fact]public async Task TicketCreationCommitsBeforePredictionAndReadsPersistedResult()
    {var ports=new TicketPorts();var handler=new CreateAnalyzedTicketHandler(ports,ports,ports,NullLogger<CreateAnalyzedTicketHandler>.Instance);await handler.HandleAsync(new("Title","Description",Guid.NewGuid()));Assert.Equal(new[]{"save","predict","read"},ports.Calls);}
    [Fact]public async Task PredictionFailureDoesNotReportCreationFailureOrFabricatePredictions()
    {var ports=new TicketPorts{Fail=true};var handler=new CreateAnalyzedTicketHandler(ports,ports,ports,NullLogger<CreateAnalyzedTicketHandler>.Instance);var saved=await handler.HandleAsync(new("Title","Description",Guid.NewGuid()));Assert.Equal(ports.Ticket.Id,saved.Id);Assert.Empty(saved.Predictions);Assert.Equal(new[]{"save","predict"},ports.Calls);}
    private sealed class ArticlePorts:IRagRetriever,ILLMService,IKnowledgeChunkReader
    {
        public Guid Document=Guid.NewGuid(),Chunk=Guid.NewGuid();public bool Empty,Forged,Changed;public List<string> Calls=[];
        private KnowledgeChunkMetadata Metadata=>new(Document,1,"SQL guide","kb/sql",TicketCategory.Database,KnowledgeDocumentKind.Procedure,KnowledgeContentFormat.PlainText,0,0,12,new string('a',64));
        public Task<RetrievedContext> RetrieveAsync(RagQuery query,CancellationToken cancellationToken=default)
        {Calls.Add("authorized-rag");Assert.Equal(Document,query.Filter!.DocumentId);return Task.FromResult(new RetrievedContext("profile",Empty?[]:[new(Document,"SQL guide",Chunk,"Check SQL",.9,Metadata)]));}
        public Task<LLMResponse> GenerateAsync(LLMRequest request,CancellationToken cancellationToken=default)
        {Calls.Add("llm");Assert.Single(request.Context);return Task.FromResult(new LLMResponse(new("SQL evidence",["Check port"],false),[new(Document,Forged?Guid.NewGuid():Chunk,1,"SQL guide","kb/sql",new string('a',64))],"Ollama","qwen3:1.7b",1,true));}
        public Task<IReadOnlyList<KnowledgeChunkDto>> ReadCurrentAsync(IReadOnlyList<Guid> ids,CancellationToken cancellationToken=default)
        {Calls.Add("recheck");return Task.FromResult<IReadOnlyList<KnowledgeChunkDto>>(Changed?[]:[new(Chunk,"Check SQL",Metadata)]);}
    }
    [Fact]public async Task ArticleAnswerUsesAuthorizedRagThenLlmThenCurrentSourceRecheck()
    {var ports=new ArticlePorts();var result=await new KnowledgeArticleAssistant(ports,ports,ports).AnswerAsync(ports.Document,"Why timeout?",default);Assert.Single(result.Citations);Assert.Equal(new[]{"authorized-rag","llm","recheck"},ports.Calls);}
    [Fact]public async Task MissingAuthorizedContextNeverReachesLlm()
    {var ports=new ArticlePorts{Empty=true};await Assert.ThrowsAsync<ResourceNotFoundException>(()=>new KnowledgeArticleAssistant(ports,ports,ports).AnswerAsync(ports.Document,"Question",default));Assert.Equal(new[]{"authorized-rag"},ports.Calls);}
    [Fact]public async Task ForgedOrRevokedCitationsAreRejected()
    {var ports=new ArticlePorts{Forged=true};await Assert.ThrowsAsync<LLMServiceException>(()=>new KnowledgeArticleAssistant(ports,ports,ports).AnswerAsync(ports.Document,"Question",default));ports.Forged=false;ports.Changed=true;await Assert.ThrowsAsync<ConflictException>(()=>new KnowledgeArticleAssistant(ports,ports,ports).AnswerAsync(ports.Document,"Question",default));}
}
