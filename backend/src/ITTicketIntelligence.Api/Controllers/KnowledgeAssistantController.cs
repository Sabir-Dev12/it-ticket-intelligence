using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Application.Assistant;
using ITTicketIntelligence.Application.LanguageModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace ITTicketIntelligence.Api.Controllers;
public sealed record ArticleQuestionRequest([Required,StringLength(2000)]string Question);
[ApiController,Route("api/assistant/knowledge"),Authorize(Roles="Admin,Agent,Support"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class KnowledgeAssistantController(KnowledgeArticleAssistant assistant):ControllerBase
{
    [HttpPost("{documentId:guid}/answers")]
    public async Task<ActionResult<LLMResponse>> Answer(Guid documentId,ArticleQuestionRequest request,CancellationToken ct)=>Ok(await assistant.AnswerAsync(documentId,request.Question,ct));
}
