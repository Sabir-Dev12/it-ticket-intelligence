using System.Text.Json;

namespace ITTicketIntelligence.Api.Mcp;

public sealed class McpHttpGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, McpSecurityOptions security)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp")) { await next(context); return; }
        var host = context.Request.Host.Host.Trim('[', ']').ToLowerInvariant();
        if (!security.AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
            (!context.Request.IsHttps && host is not ("localhost" or "127.0.0.1" or "::1")) ||
            (context.Request.Headers.TryGetValue("Origin", out var origin) && (origin.Count != 1 || !security.AllowedOrigins.Contains(origin.ToString(), StringComparer.Ordinal))))
        { context.Response.StatusCode = 403; return; }
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }
            await using var bytes = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
            {
                if (bytes.Length + count > 16384) { context.Response.StatusCode = 413; return; }
                await bytes.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
            }
            try
            {
                using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicates(document.RootElement);
            }
            catch (JsonException) { context.Response.StatusCode = 400; return; }
            bytes.Position = 0; var original = context.Request.Body; context.Request.Body = bytes;
            try { await next(context); } finally { context.Request.Body = original; await bytes.DisposeAsync(); }
            return;
        }
        await next(context);
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException(); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
