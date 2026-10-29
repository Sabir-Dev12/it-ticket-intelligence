using ITTicketIntelligence.Api.Contracts.Knowledge;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
[RequestSizeLimit(1024 * 1024)]
[ProducesResponseType(typeof(ValidationProblemDetails), 400, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 404, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 409, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 503, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 500, "application/problem+json")]
public sealed class KnowledgeController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), 201)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Create(CreateKnowledgeRequest request,
        [FromServices] ICommandHandler<CreateKnowledgeCommand, KnowledgeDocumentDto> handler, CancellationToken cancellationToken)
    {
        var document = await handler.HandleAsync(new(request.Title, request.Content, request.Source, request.Category,
            request.Kind, request.Format, request.CreatedById), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, document);
    }

    [HttpGet]
    [ProducesResponseType(typeof(KnowledgePage), 200)]
    public async Task<ActionResult<KnowledgePage>> List([FromQuery] GetKnowledgeRequest request,
        [FromServices] IQueryHandler<GetKnowledgePageQuery, KnowledgePage> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new(request.Page, request.PageSize, request.Search, request.Category, request.Kind), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), 200)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Get(Guid id,
        [FromServices] IQueryHandler<GetKnowledgeQuery, KnowledgeDocumentDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new(id), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), 200)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Update(Guid id, UpdateKnowledgeRequest request,
        [FromServices] ICommandHandler<UpdateKnowledgeCommand, KnowledgeDocumentDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new(id, request.Title, request.Content, request.Source, request.Category,
            request.Kind, request.Format, request.Version), cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int version,
        [FromServices] ICommandHandler<DeleteKnowledgeCommand, bool> handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new(id, version), cancellationToken);
        return NoContent();
    }
}
