using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ITTicketIntelligence.Api.Realtime;

namespace ITTicketIntelligence.Api.Mcp;

public sealed class McpTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, McpSecurityOptions security) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpToken";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasHeader = Request.Headers.TryGetValue("Authorization", out var authorization);
        // Browser WebSocket/SSE APIs cannot set Authorization. Never accept query tokens on other routes.
        if (!hasHeader && Request.Path.Equals(new PathString(TicketHub.Route)) &&
            (Context.WebSockets.IsWebSocketRequest || Request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.Ordinal)) &&
            Request.Query.TryGetValue("access_token", out var queryToken) && queryToken.Count == 1)
            authorization = "Bearer " + queryToken[0];
        if (authorization.Count == 0) return Jwt();
        if (authorization.Count != 1 || authorization[0] is not { } header || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Fail();
        var token = header[7..];
        if (token.Length is < 32 or > 512 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl)) return Fail();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        McpCredential? matched = null;
        foreach (var credential in security.Clients)
            if (CryptographicOperations.FixedTimeEquals(digest, credential.TokenHash)) matched = credential;
        if (matched is null) return Jwt();
        var caller = matched.Caller;
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, caller.Subject)];
        if (matched.UserId is { } userId) claims.Add(new("assistant.user", userId.ToString()));
        claims.AddRange(caller.Permissions.Select(p => new Claim("mcp.permission", p)));
        claims.AddRange(caller.TicketIds.Select(id => new Claim("mcp.ticket", id.ToString())));
        if (caller.AllTickets) claims.Add(new("mcp.allTickets", "true"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
    private async Task<AuthenticateResult> Jwt()
    {
        var session = await Context.AuthenticateAsync(Security.SessionSecurity.Scheme);
        return session.Succeeded ? AuthenticateResult.Success(new AuthenticationTicket(session.Principal!, session.Properties!, SchemeName)) : AuthenticateResult.NoResult();
    }
    private Task<AuthenticateResult> Fail()
    {
        Logger.LogWarning("MCP authentication denied");
        return Task.FromResult(AuthenticateResult.Fail("Invalid MCP credentials."));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401; Response.Headers.WWWAuthenticate = "Bearer realm=\"it-ticket-mcp\"";
        return Task.CompletedTask;
    }
}
