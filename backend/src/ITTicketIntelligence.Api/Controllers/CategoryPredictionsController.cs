using System.ComponentModel.DataAnnotations;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

public sealed record PredictCategoryRequest
{
    [Required, StringLength(160)] public required string Title { get; init; }
    [Required, StringLength(8000)] public required string Description { get; init; }
}

[ApiController]
[Route("api/predictions/category")]
public sealed class CategoryPredictionsController(IQueryHandler<PredictTicketCategoryQuery, TicketCategoryPrediction> handler) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(TicketCategoryPrediction), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), 500, "application/problem+json")]
    public async Task<ActionResult<TicketCategoryPrediction>> Predict(PredictCategoryRequest request, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new PredictTicketCategoryQuery(request.Title, request.Description), cancellationToken));
}
