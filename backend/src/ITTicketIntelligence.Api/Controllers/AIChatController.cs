using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using ITTicketIntelligence.Application.LanguageModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

public sealed record CreateAIChatRequest([Required,StringLength(160)] string Title);
public sealed record AIChatRequest([Required,StringLength(8000)] string Message,[StringLength(128)] string? Model=null);

[ApiController,Route("api/ai/chat"),Authorize(Roles="Admin,Support,Agent")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[RequestSizeLimit(64*1024)]
public sealed class AIChatController(AIChatConversations conversations,IAIChatService chat,ILogger<AIChatController> logger) : ControllerBase
{
    private Guid Owner => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("conversations")]
    public async Task<ActionResult<AIChatPage>> List([FromQuery,Range(1,10000)]int page=1,CancellationToken ct=default)=>Ok(await conversations.ListAsync(Owner,page,ct));
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct) { var result=await chat.HealthAsync(ct);return StatusCode(result.Healthy?200:503,result); }
    [HttpPost("conversations")]
    public async Task<ActionResult<AIChatSnapshot>> Create(CreateAIChatRequest request,CancellationToken ct)
    {
        var result=await conversations.CreateAsync(Owner,request.Title,ct);
        return CreatedAtAction(nameof(Get),new{id=result.Id},result);
    }
    [HttpGet("conversations/{id:guid}")]
    public async Task<ActionResult<AIChatSnapshot>> Get(Guid id,CancellationToken ct)=>Ok(await conversations.GetAsync(id,Owner,ct));
    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<IActionResult> Send(Guid id,AIChatRequest request,CancellationToken ct)=>Ok(new { text=await conversations.SendAsync(id,Owner,request.Message,request.Model,ct),conversationId=id });
    [HttpPost("conversations/{id:guid}/stream")]
    public async Task Stream(Guid id,AIChatRequest request,CancellationToken ct)
    {
        try
        {
            await foreach(var item in conversations.StreamAsync(id,Owner,request.Message,request.Model,ct))
            {
                Response.ContentType="application/x-ndjson";
                await Response.WriteAsync(JsonSerializer.Serialize(item,new JsonSerializerOptions(JsonSerializerDefaults.Web))+"\n",ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
        catch(Exception error)
        {
            if(!Response.HasStarted) throw;
            logger.LogWarning("Chat stream interrupted with {ErrorType}",error.GetType().Name);
            if(!ct.IsCancellationRequested)await Response.WriteAsync("{\"type\":\"error\",\"code\":\"generation_failed\"}\n",ct);
        }
    }
}
