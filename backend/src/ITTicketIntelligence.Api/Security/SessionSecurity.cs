using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Domain.Enums;
namespace ITTicketIntelligence.Api.Security;
public static class SessionSecurity
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    private static SymmetricSecurityKey Key()
    {
        var secret = Environment.GetEnvironmentVariable("JWT__SigningKey");
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 64) throw new InvalidOperationException("Configure JWT__SigningKey with at least 64 random bytes outside source control.");
        return new(Encoding.UTF8.GetBytes(secret));
    }
    public static ClaimsPrincipal Principal(SessionUser user, DateTimeOffset? expires = null)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier,user.Id.ToString()),new(ClaimTypes.Name,user.Name),new(ClaimTypes.Role,user.Role.ToString()),new("session.user",user.Id.ToString()),new("assistant.user",user.Id.ToString()),new("session.version",user.Version),new("mcp.permission","tickets.read"),new("mcp.permission","tickets.history.read")];
        if (user.Role != UserRole.User) claims.AddRange(new[] { "knowledge.read", "teams.read", "tickets.search" }.Select(p=>new Claim("mcp.permission",p)));
        if (user.Role == UserRole.Admin) claims.Add(new("mcp.permission","system.status.read"));
        if (user.Role is UserRole.Admin or UserRole.Support) claims.Add(new("mcp.allTickets","true"));
        claims.AddRange(user.TicketIds.Select(id=>new Claim("mcp.ticket",id.ToString())));
        if (expires.HasValue) claims.Add(new("session.expires", expires.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new(new ClaimsIdentity(claims,Scheme));
    }
    public static string Issue(SessionUser user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: "it-ticket-intelligence", audience: "it-ticket-dashboard",
        claims: new[] { new Claim("session.user",user.Id.ToString()),new Claim("session.version",user.Version),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString()) },
        notBefore: DateTime.UtcNow, signingCredentials: new SigningCredentials(Key(),SecurityAlgorithms.HmacSha256)));
    public static void AddSessions(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddAuthentication(o=>{o.DefaultAuthenticateScheme=Scheme;o.DefaultChallengeScheme=Scheme;}).AddJwtBearer(Scheme,o=>
        {
            o.MapInboundClaims=false;
            o.TokenValidationParameters=new TokenValidationParameters { ValidateIssuer=true,ValidIssuer="it-ticket-intelligence",ValidateAudience=true,ValidAudience="it-ticket-dashboard",ValidateLifetime=true,RequireExpirationTime=false,ValidateIssuerSigningKey=true,RequireSignedTokens=true,ValidAlgorithms=[SecurityAlgorithms.HmacSha256],ClockSkew=TimeSpan.FromSeconds(15),IssuerSigningKeyResolver=(_,_,_,_)=>[Key()] };
            o.Events=new JwtBearerEvents
            {
                OnMessageReceived=c=>{ if(c.Request.Path=="/hubs/tickets" && (c.HttpContext.WebSockets.IsWebSocketRequest || c.Request.Headers.Accept.ToString().Contains("text/event-stream"))) c.Token=c.Request.Query["access_token"]; return Task.CompletedTask; },
                OnTokenValidated=async c=>
                {
                    var service=c.HttpContext.RequestServices.GetRequiredService<IUserSessions>();
                    var user=Guid.TryParse(c.Principal?.FindFirstValue("session.user"),out var id)?await service.FindAsync(id,c.HttpContext.RequestAborted):null;
                    if(user is null || user.Version!=c.Principal?.FindFirstValue("session.version"))c.Fail("Session revoked.");else c.Principal=Principal(user,c.SecurityToken.ValidTo == DateTime.MinValue ? null : new DateTimeOffset(c.SecurityToken.ValidTo));
                }
            };
        });
    }
}
