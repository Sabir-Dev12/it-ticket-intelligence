using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Application.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

public sealed record CreateAssistantConversationRequest
{
    public required Guid RequestId { get; init; }
    [Required, StringLength(2000)] public required string Question { get; init; }
    public Guid? TicketId { get; init; }
}
public sealed record SendAssistantMessageRequest
{
    public required Guid RequestId { get; init; }
    [Required, StringLength(2000)] public required string Content { get; init; }
}

[ApiController, Route("api/assistant/conversations")]
[Authorize(Policy = "AssistantAccess")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(8192)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
[ProducesResponseType(typeof(ProblemDetails), 503)]
public sealed class AssistantController(IAssistantService assistant) : ControllerBase
{
    private AssistantIdentity Caller()
    {
        var scope = McpSecurityOptions.Caller(User);
        return new(Guid.Parse(User.FindFirstValue("assistant.user")!), scope.AllTickets, scope.TicketIds);
    }
    [HttpGet]
    public async Task<ActionResult<AssistantConversationPage>> List([FromQuery, Range(1, 10000)] int page = 1, CancellationToken cancellationToken = default) =>
        Ok(await assistant.ListAsync(Caller(), page, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AssistantConversation>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await assistant.GetAsync(id, Caller(), cancellationToken));
    [HttpPost]
    [ProducesResponseType(typeof(AssistantConversation), 201)]
    public async Task<ActionResult<AssistantConversation>> Create(CreateAssistantConversationRequest request, CancellationToken cancellationToken)
    {
        var result = await assistant.CreateAsync(request.RequestId, request.Question, request.TicketId, Caller(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<AssistantConversation>> Send(Guid id, SendAssistantMessageRequest request, CancellationToken cancellationToken) =>
        Ok(await assistant.SendAsync(id, request.RequestId, request.Content, Caller(), cancellationToken));
}
