using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
namespace ITTicketIntelligence.Api.Controllers;
public sealed record CreateAccountRequest([Required,StringLength(100)] string Name,[Required,EmailAddress,StringLength(254)] string Email,[Required,StringLength(256,MinimumLength=14)] string Password, UserRole Role);
public sealed record ChangeAccountRequest { public required UserRole Role { get; init; } public required bool IsActive { get; init; } }
public sealed record PasswordRequest([Required,StringLength(256,MinimumLength=14)]string Password);
[ApiController,Route("api/admin/users"),Microsoft.AspNetCore.Authorization.Authorize(Roles="Admin")]
public sealed class AdminUsersController(IUserSessions sessions) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<UserAccount>>> List(CancellationToken ct)=>Ok(await sessions.ListAsync(ct));
    [HttpPost] public async Task<ActionResult<UserAccount>> Create(CreateAccountRequest request,CancellationToken ct)=>Ok(await sessions.CreateAsync(Guid.Parse(User.FindFirstValue("session.user")!),request.Name,request.Email,request.Password,request.Role,ct));
    [HttpPatch("{id:guid}")] public async Task<IActionResult> Change(Guid id,ChangeAccountRequest request,CancellationToken ct) { await sessions.ChangeAsync(Guid.Parse(User.FindFirstValue("session.user")!),id,request.Role,request.IsActive,ct);return NoContent(); }
    [HttpPost("{id:guid}/password")]public async Task<IActionResult> Password(Guid id,PasswordRequest request,CancellationToken ct){await sessions.PasswordAsync(Guid.Parse(User.FindFirstValue("session.user")!),id,request.Password,ct);return NoContent();}
}
