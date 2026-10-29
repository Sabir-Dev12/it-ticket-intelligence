using ITTicketIntelligence.Api.Contracts.Tickets;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Tickets.Commands;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using Microsoft.AspNetCore.Mvc;
using ITTicketIntelligence.Application.Agents;

namespace ITTicketIntelligence.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[ProducesResponseType(typeof(ValidationProblemDetails), 400, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 404, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 409, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 503, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), 500, "application/problem+json")]
public sealed class TicketsController : ControllerBase
{
    /// <summary>Reads assigned/active teams and the latest saved analysis, without executing AI.</summary>
    [HttpGet("{id:guid}/workspace")]
    [ProducesResponseType(typeof(TicketWorkspaceDto), 200)]
    public async Task<ActionResult<TicketWorkspaceDto>> Workspace(Guid id,
        [FromServices] IQueryHandler<GetTicketWorkspaceQuery, TicketWorkspaceDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new(id), cancellationToken));

    /// <summary>Investigates a ticket with ML, RAG and read-only tools. Returns proposals requiring human approval.</summary>
    [HttpPost("{id:guid}/investigate")]
    [ProducesResponseType(typeof(TicketAgentResult), 200)]
    public async Task<ActionResult<TicketAgentResult>> Investigate(Guid id,
        [FromServices] ITicketAgent agent, CancellationToken cancellationToken) =>
        Ok(await agent.AnalyzeAsync(id, cancellationToken));

    /// <summary>Runs the three ML predictors and appends their results without applying recommendations.</summary>
    [HttpPost("{id:guid}/analyze")]
    [ProducesResponseType(typeof(IntelligenceResult), 200)]
    public async Task<ActionResult<IntelligenceResult>> Analyze(Guid id,
        [FromServices] ITicketIntelligencePipeline pipeline, CancellationToken cancellationToken) =>
        Ok(await pipeline.AnalyzeAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(TicketDto), 201)]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketRequest request,
        [FromServices] ICommandHandler<CreateTicketCommand, TicketDto> handler, CancellationToken cancellationToken)
    {
        var ticket = await handler.HandleAsync(new CreateTicketCommand(request.Title, request.Description, request.CreatedById, request.Category, request.Priority, request.AssignedTeamId, request.AssignedUserId), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = ticket.Id }, ticket);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketDto), 200)]
    public async Task<ActionResult<TicketDto>> Get(Guid id, [FromServices] IQueryHandler<GetTicketQuery, TicketDto> handler,
        CancellationToken cancellationToken) => Ok(await handler.HandleAsync(new GetTicketQuery(id), cancellationToken));

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TicketSummaryDto>), 200)]
    public async Task<ActionResult<PagedResult<TicketSummaryDto>>> List([FromQuery] GetTicketsRequest request,
        [FromServices] IQueryHandler<GetTicketsQuery, PagedResult<TicketSummaryDto>> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new GetTicketsQuery(request.Page, request.PageSize, request.Search, request.Status,
            request.Priority, request.Category, request.AssignedTeamId, request.CreatedById, request.IncludeArchived, HttpContext.Items["allowedTickets"] as Guid[]), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TicketDto), 200)]
    public async Task<ActionResult<TicketDto>> Update(Guid id, UpdateTicketRequest request,
        [FromServices] ICommandHandler<UpdateTicketCommand, TicketDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new UpdateTicketCommand(id, request.Title, request.Description,
            request.Category, request.Priority, request.Revision, request.Assignment is null ? null : new TicketAssignment(request.Assignment.TeamId), request.Assignee is null ? null : new TicketUserAssignment(request.Assignee.UserId)), cancellationToken));

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(TicketDto), 200)]
    public async Task<ActionResult<TicketDto>> ChangeStatus(Guid id, ChangeTicketStatusRequest request,
        [FromServices] ICommandHandler<ChangeTicketStatusCommand, TicketDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new ChangeTicketStatusCommand(id, request.Status, request.Resolution, request.Revision), cancellationToken));

    [HttpPatch("{id:guid}/assignment")]
    [ProducesResponseType(typeof(TicketDto), 200)]
    public async Task<ActionResult<TicketDto>> Assign(Guid id, AssignTicketTeamRequest request,
        [FromServices] ICommandHandler<AssignTicketTeamCommand, TicketDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new AssignTicketTeamCommand(id, request.TeamId, request.Revision), cancellationToken));

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(AddCommentResult), 201)]
    public async Task<ActionResult<AddCommentResult>> AddComment(Guid id, AddTicketCommentRequest request,
        [FromServices] ICommandHandler<AddTicketCommentCommand, AddCommentResult> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new AddTicketCommentCommand(id, request.AuthorId, request.Message, request.Revision), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Archive(Guid id, [FromQuery] int revision,
        [FromServices] ICommandHandler<ArchiveTicketCommand, TicketDto> handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchiveTicketCommand(id, revision), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(TicketDto), 200)]
    public async Task<ActionResult<TicketDto>> Restore(Guid id, RestoreTicketRequest request,
        [FromServices] ICommandHandler<RestoreTicketCommand, TicketDto> handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new RestoreTicketCommand(id, request.Revision), cancellationToken));
}
