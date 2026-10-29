using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

public sealed record PredictPriorityRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(8000)] public required string Description { get; init; }
    public required TicketCategory Category { get; init; }
}

[ApiController]
[Route("api/predictions/priority")]
public sealed class PriorityPredictionsController(IQueryHandler<PredictTicketPriorityQuery, TicketPriorityPrediction> handler) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(TicketPriorityPrediction), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), 500, "application/problem+json")]
    public async Task<ActionResult<TicketPriorityPrediction>> Predict(PredictPriorityRequest request, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new PredictTicketPriorityQuery(request.Title, request.Description, request.Category), cancellationToken));
}
