using System.Security.Claims;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Mcp.Contracts;

namespace ITTicketIntelligence.Api.Mcp;

public sealed class McpCredential
{
    public McpCredential(string tokenHash, McpCaller caller, Guid? userId = null)
    {
        if (tokenHash.Length != 64 || tokenHash.Any(c => !char.IsAsciiHexDigitLower(c))) throw new ArgumentException("Invalid MCP token hash configuration.");
        if (userId == Guid.Empty) throw new ArgumentException("Invalid assistant user mapping.");
        TokenHash = Convert.FromHexString(tokenHash); Caller = caller; UserId = userId;
    }
    [JsonIgnore] public byte[] TokenHash { get; }
    public McpCaller Caller { get; }
    public Guid? UserId { get; }
}
public sealed class McpSecurityOptions
{
    public McpSecurityOptions(IEnumerable<McpCredential>? clients = null, IEnumerable<string>? allowedHosts = null, IEnumerable<string>? allowedOrigins = null)
    {
        Clients = Array.AsReadOnly((clients ?? []).ToArray());
        AllowedHosts = Array.AsReadOnly((allowedHosts ?? ["localhost", "127.0.0.1", "::1"]).ToArray());
        AllowedOrigins = Array.AsReadOnly((allowedOrigins ?? []).ToArray());
        if (Clients.Count > 16 || Clients.Select(c => Convert.ToHexString(c.TokenHash)).Distinct().Count() != Clients.Count ||
            Clients.Select(c => c.Caller.Subject).Distinct().Count() != Clients.Count || AllowedHosts.Count == 0 ||
            AllowedHosts.Any(h => string.IsNullOrWhiteSpace(h) || h.Contains('*') || h.Contains('/') || h.Any(char.IsWhiteSpace)) ||
            AllowedOrigins.Any(o => !Uri.TryCreate(o, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.GetLeftPart(UriPartial.Authority) != o))
            throw new ArgumentException("Invalid MCP security configuration.");
    }
    [JsonIgnore] public IReadOnlyList<McpCredential> Clients { get; }
    public IReadOnlyList<string> AllowedHosts { get; }
    public IReadOnlyList<string> AllowedOrigins { get; }
    public static McpSecurityOptions FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var clients = new List<McpCredential>();
        for (var i = 0; i < 16; i++)
        {
            var prefix = $"MCP__Clients__{i}__"; var hash = read(prefix + "TokenHash");
            if (string.IsNullOrWhiteSpace(hash)) continue;
            try
            {
                var subject = read(prefix + "Subject") ?? "";
                var permissions = Split(read(prefix + "Permissions"));
                var ids = Split(read(prefix + "TicketIds")).Select(Guid.Parse).ToArray();
                var all = read(prefix + "AllTickets") ?? "false";
                if (!bool.TryParse(all, out var allTickets)) throw new ArgumentException();
                Guid? userId = read(prefix + "UserId") is { Length: > 0 } rawUser ? Guid.Parse(rawUser) : null;
                clients.Add(new McpCredential(hash, new McpCaller(subject, permissions, ids, allTickets), userId));
            }
            catch { throw new ArgumentException("Invalid MCP client configuration. Check the environment fields without logging their values."); }
        }
        return new(clients, read("MCP__AllowedHosts") is { } hosts ? Split(hosts) : null, Split(read("MCP__AllowedOrigins")));
    }
    private static string[] Split(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static McpCaller Caller(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        return new(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", user.FindAll("mcp.permission").Select(c => c.Value),
            user.FindAll("mcp.ticket").Select(c => Guid.Parse(c.Value)), user.HasClaim("mcp.allTickets", "true"));
    }
}
