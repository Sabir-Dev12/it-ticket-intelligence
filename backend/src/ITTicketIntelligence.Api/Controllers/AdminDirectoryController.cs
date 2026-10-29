using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ITTicketIntelligence.Application.Security;
using Microsoft.AspNetCore.Mvc;
namespace ITTicketIntelligence.Api.Controllers;
public sealed record CreateTeamRequest([Required,StringLength(100)]string Name,[StringLength(1000)]string? Description);
[ApiController,Route("api/admin")]
public sealed class AdminDirectoryController(IAdminDirectory directory,IConfiguration configuration) : ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue("session.user")!);
    [HttpGet("teams")]public async Task<IActionResult> Teams(CancellationToken ct)=>Ok(await directory.TeamsAsync(ct));
    [HttpPost("teams")]public async Task<IActionResult> Create(CreateTeamRequest request,CancellationToken ct)=>Ok(await directory.CreateTeamAsync(Actor,request.Name,request.Description,ct));
    [HttpPut("teams/{team:guid}/members/{userId:guid}")]public async Task<IActionResult> Add(Guid team,Guid userId,CancellationToken ct){await directory.MembershipAsync(Actor,team,userId,true,ct);return NoContent();}
    [HttpDelete("teams/{team:guid}/members/{userId:guid}")]public async Task<IActionResult> Remove(Guid team,Guid userId,CancellationToken ct){await directory.MembershipAsync(Actor,team,userId,false,ct);return NoContent();}
    [HttpGet("audit")]public async Task<IActionResult> Audit([FromQuery,Range(1,10000)]int page=1,CancellationToken ct=default)=>Ok(await directory.AuditAsync(page,ct));
    [HttpGet("ml-configuration")]public IActionResult Models()=>Ok(new { categoryMinimumConfidence=configuration.GetValue<double>("MachineLearning:CategoryMinimumConfidence",.8),priorityMinimumConfidence=configuration.GetValue<double>("MachineLearning:PriorityMinimumConfidence",.8),training="offline",configuration="environment" });
}
