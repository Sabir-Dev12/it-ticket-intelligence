using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

public sealed record RetrieveContextRequest
{
    [Required, StringLength(2000)] public required string Question { get; init; }
    [Range(1, 20)] public int Limit { get; init; } = 5;
    [Range(-1d, 1d)] public double MinimumScore { get; init; } = 0.35;
    public Guid? DocumentId { get; init; }
    public TicketCategory? Category { get; init; }
    public KnowledgeDocumentKind? Kind { get; init; }
}

[ApiController]
[Route("api/rag")]
[ProducesResponseType(typeof(ValidationProblemDetails), 400, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 404, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 409, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 503, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 500, "application/problem+json")]
public sealed class RagController : ControllerBase
{
    [HttpPost("documents/{id:guid}/index")]
    [ProducesResponseType(typeof(KnowledgeIndexJob), 202)]
    public async Task<IActionResult> Index(Guid id, [FromServices] IKnowledgeIndexQueue queue,
        CancellationToken cancellationToken) { var job=await queue.EnqueueAsync(id,cancellationToken);return AcceptedAtAction(nameof(Job),new{id=job.Id},job); }

    [HttpGet("index-jobs/{id:guid}")]
    public async Task<IActionResult> Job(Guid id,[FromServices] IKnowledgeIndexQueue queue,CancellationToken ct)
    {var job=await queue.GetAsync(id,ct);return job is null?NotFound():Ok(job);}

    [HttpPost("retrieve")]
    [ProducesResponseType(typeof(RetrievedContext), 200)]
    public async Task<ActionResult<RetrievedContext>> Retrieve(RetrieveContextRequest request,
        [FromServices] IRagRetriever retriever, CancellationToken cancellationToken) =>
        Ok(await retriever.RetrieveAsync(new RagQuery(request.Question, request.Limit, request.MinimumScore,
            new KnowledgeVectorFilter(request.DocumentId, request.Category, request.Kind)), cancellationToken));
}
