using System.Text.Json;
using System.Text.Json.Nodes;
using ITTicketIntelligence.Application.Mcp.Contracts;
using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace ITTicketIntelligence.Api.Mcp;

public static class McpServerRegistration
{
    public static IServiceCollection AddControlledMcp(this IServiceCollection services)
    {
        services.AddSingleton(_ => McpSecurityOptions.FromEnvironment());
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, McpTokenHandler>(McpTokenHandler.SchemeName, _ => { });
        services.AddAuthorization(options => options.AddPolicy("McpAccess", policy => policy.AddAuthenticationSchemes(McpTokenHandler.SchemeName).RequireAuthenticatedUser()));
        services.AddMcpServer().WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((context, token) =>
            {
                token.ThrowIfCancellationRequested();
                var caller = McpSecurityOptions.Caller(context.User);
                var tools = McpToolCatalog.All.Where(t => caller.Permissions.Contains(t.Permission)).Select(t => new Tool
                {
                    Name = t.Name, Description = t.Description, InputSchema = t.InputSchema, OutputSchema = t.OutputSchema,
                    Annotations = new() { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
                    Meta = new JsonObject { ["requiredPermission"] = t.Permission }
                }).ToArray();
                return ValueTask.FromResult(new ListToolsResult { Tools = tools });
            })
            .WithCallToolHandler(async (context, token) =>
            {
                try
                {
                    var caller = McpSecurityOptions.Caller(context.User);
                    var service = context.Services!.GetRequiredService<IMcpToolService>();
                    var result = await service.CallAsync(context.Params.Name,
                        JsonSerializer.SerializeToElement(context.Params.Arguments ?? new Dictionary<string, JsonElement>(), McpToolCatalog.Json), caller, token);
                    return Reply(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch
                {
                    return Reply(new(JsonSerializer.SerializeToElement(new { error = new McpError("Unavailable", "The controlled tool service is unavailable.", Guid.NewGuid()) }, McpToolCatalog.Json), true));
                }
            });
        return services;
    }
    public static void MapControlledMcp(this WebApplication app) => app.MapMcp("/mcp").RequireAuthorization("McpAccess");
    private static CallToolResult Reply(McpToolResponse result) => new()
    {
        IsError = result.IsError, StructuredContent = result.Content,
        Content = [new TextContentBlock { Text = result.Content.GetRawText() }]
    };
}
