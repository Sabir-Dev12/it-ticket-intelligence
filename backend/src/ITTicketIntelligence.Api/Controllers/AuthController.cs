using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
namespace ITTicketIntelligence.Api.Controllers;
public sealed record LoginRequest([Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(256)] string Password);
[ApiController, Route("api/auth")]
public sealed class AuthController(IUserSessions sessions) : ControllerBase
{
    [HttpPost("login")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await sessions.LoginAsync(request.Email, request.Password, ct);
        if (user is null) return Unauthorized(new ProblemDetails { Status = 401, Title = "Email or password is invalid." });
        var accessToken = SessionSecurity.Issue(user);
        return Ok(new { accessToken, tokenType = "Bearer", expiresIn = (int?)null, user = new { user.Id, user.Name, user.Email, role = user.Role.ToString() } });
    }
    [HttpGet("me")]
    public IActionResult Me() => User.HasClaim(c => c.Type == "session.user") ? Ok(new { id = User.FindFirstValue("session.user"), name = User.Identity!.Name, role = User.FindFirstValue(ClaimTypes.Role) }) : Unauthorized();
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Guid.TryParse(User.FindFirstValue("session.user"), out var id)) await sessions.AuditAsync(id, "Auth.Logout", ct);
        if (Guid.TryParse(User.FindFirstValue("session.user"), out var logoutId)) await sessions.RevokeAsync(logoutId, ct); return Ok(new { signedOut = true });
    }
}
