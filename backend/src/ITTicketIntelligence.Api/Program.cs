using ITTicketIntelligence.Api.Security;
using ITTicketIntelligence.Application;
using ITTicketIntelligence.Infrastructure;
using ITTicketIntelligence.Api.Errors;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Infrastructure.MachineLearning;
using ITTicketIntelligence.Api.Mcp;
using ITTicketIntelligence.Api.Realtime;

namespace ITTicketIntelligence.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApplication();
        builder.Services.AddScoped<Security.RestAccessFilter>();
        builder.Services.AddSessions(builder.Environment);
        builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
        builder.Services.AddSingleton(provider => new IntelligenceReviewPolicy(
            provider.GetRequiredService<IConfiguration>().GetValue<double>("MachineLearning:CategoryMinimumConfidence", 0.8),
            provider.GetRequiredService<IConfiguration>().GetValue<double>("MachineLearning:PriorityMinimumConfidence", 0.8)));
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITTicketIntelligence.Application.Knowledge.Retrieval.IKnowledgeAccessContext,Security.KnowledgeAccessContext>();
        builder.Services.AddTicketCategoryPrediction();
        builder.Services.AddTicketPriorityPrediction();
        builder.Services.AddTicketTeamPrediction();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddControllers(o => o.Filters.AddService<Security.RestAccessFilter>()).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        }).ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = 400, Title = "Requête invalide", Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            var result = new BadRequestObjectResult(problem);
            result.ContentTypes.Add("application/problem+json");
            return result;
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        builder.Services.AddOpenApi();
        builder.Services.AddControlledMcp();
        builder.Services.AddTicketRealtime();
        builder.Services.AddAuthorization(options => options.AddPolicy("AssistantAccess", policy => policy
            .AddAuthenticationSchemes(McpTokenHandler.SchemeName).RequireAuthenticatedUser()
            .RequireClaim("mcp.permission", "tickets.read").RequireClaim("assistant.user")));
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

        var app = builder.Build();
        _ = app.Services.GetRequiredService<IntelligenceReviewPolicy>();
        // Fail at startup on an absent/corrupt artifact; no training or lazy disk loading per HTTP request.
        _ = app.Services.GetRequiredService<ITicketCategoryPredictor>();
        _ = app.Services.GetRequiredService<ITicketPriorityPredictor>();
        _ = app.Services.GetRequiredService<ITicketTeamPredictor>();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/assistant")) context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.UseMiddleware<McpHttpGuard>();
        app.UseMiddleware<RealtimeHttpGuard>();
        app.UseAuthentication();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControlledMcp();
        app.MapTicketRealtime();
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();

        if (Environment.GetEnvironmentVariable("AUTH__BootstrapEmail") is { Length: > 0 } email && Environment.GetEnvironmentVariable("AUTH__BootstrapPassword") is { Length: > 0 } password)
        {
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITTicketIntelligence.Application.Security.IUserSessions>().BootstrapAsync(email, password, CancellationToken.None).GetAwaiter().GetResult();
        }
        app.Run();
    }
}
