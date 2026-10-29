using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
namespace ITTicketIntelligence.Application.Assistant;
public sealed class KnowledgeArticleAssistant(IRagRetriever rag,ILLMService llm,IKnowledgeChunkReader reader)
{
    public async Task<LLMResponse> AnswerAsync(Guid documentId,string question,CancellationToken ct)
    {
        if(documentId==Guid.Empty || string.IsNullOrWhiteSpace(question) || question.Length>2000)
            throw new RequestValidationException(new Dictionary<string,string[]>{["question"]=["Choose an article and ask a question of 1 to 2000 characters."]});
        var context=await rag.RetrieveAsync(new(question,5,-1,new KnowledgeVectorFilter(DocumentId:documentId)),ct);
        if(context.Results.Count==0)throw new ResourceNotFoundException("Accessible indexed article context");
        var answer=await llm.GenerateAsync(new LLMRequest(question,context),ct);
        if(answer.Citations.Any(c=>c.DocumentId!=documentId || !context.Results.Any(k=>k.ChunkId==c.ChunkId && k.Metadata.DocumentVersion==c.DocumentVersion && k.Metadata.ContentHash==c.ContentHash)))
            throw new LLMServiceException(LLMFailureKind.InvalidResponse);
        var current=await reader.ReadCurrentAsync(answer.Citations.Select(c=>c.ChunkId).ToArray(),ct);
        if(answer.Citations.Any(c=>!current.Any(k=>k.Id==c.ChunkId && k.Metadata.DocumentId==c.DocumentId && k.Metadata.DocumentVersion==c.DocumentVersion && k.Metadata.ContentHash==c.ContentHash)))
            throw new ConflictException("The article or its permissions changed. Ask again using its current version.");
        return answer;
    }
}
