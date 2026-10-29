using System.Security.Claims;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace ITTicketIntelligence.Api.Controllers;
public sealed record KnowledgePermissionsRequest(bool IsRestricted);
[ApiController,Route("api/knowledge"),Authorize(Roles="Admin")]
public sealed class KnowledgePermissionsController(IKnowledgePermissions permissions):ControllerBase
{
    [HttpPatch("{id:guid}/permissions")]
    public async Task<IActionResult> Set(Guid id,KnowledgePermissionsRequest request,CancellationToken ct)
    {await permissions.SetRestrictedAsync(id,request.IsRestricted,Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),ct);return NoContent();}
}
