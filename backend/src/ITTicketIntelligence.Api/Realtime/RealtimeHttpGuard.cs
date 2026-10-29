namespace ITTicketIntelligence.Api.Realtime;

public sealed class RealtimeHttpGuard(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(TicketHub.Route) && context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            var configured = Environment.GetEnvironmentVariable("Realtime__AllowedOrigins");
            var allowed = configured is not null ? configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : environment.IsDevelopment() ? ["http://127.0.0.1:5173", "http://localhost:5173"] : Array.Empty<string>();
            if (origin.Count != 1 || !allowed.Contains(origin.ToString(), StringComparer.Ordinal))
            { context.Response.StatusCode = 403; return; }
        }
        await next(context);
    }
}
