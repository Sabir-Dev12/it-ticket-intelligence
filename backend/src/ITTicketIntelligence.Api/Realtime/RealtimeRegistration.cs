using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Application.Realtime;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json.Serialization;

namespace ITTicketIntelligence.Api.Realtime;

public static class RealtimeRegistration
{
    public static IServiceCollection AddTicketRealtime(this IServiceCollection services)
    {
        services.AddSingleton<Security.LiveConnections>();
        services.AddAuthorization(o => o.AddPolicy("TicketRealtime", p => p.AddAuthenticationSchemes(McpTokenHandler.SchemeName)
            .RequireAuthenticatedUser().RequireClaim("mcp.permission", "tickets.read")));
        services.AddSignalR(o => { o.EnableDetailedErrors = false; o.MaximumReceiveMessageSize = 8192; o.MaximumParallelInvocationsPerClient = 1; })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.Replace(ServiceDescriptor.Singleton<ITicketEventSink, SignalRTicketEventSink>());
        return services;
    }
    public static void MapTicketRealtime(this WebApplication app) => app.MapHub<TicketHub>(TicketHub.Route,
        o => { o.CloseOnAuthenticationExpiration = true; o.AllowStatefulReconnects = false; });
}
