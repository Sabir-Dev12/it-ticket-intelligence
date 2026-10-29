using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Infrastructure.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Infrastructure.LanguageModels;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Infrastructure.Persistence.Agents;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Infrastructure.Mcp;

namespace ITTicketIntelligence.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<HttpLLMService>(provider =>
        {
            var options = LLMOptions.FromEnvironment();
            return new HttpLLMService(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), options,
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetService<ILogger<HttpLLMService>>() ?? NullLogger<HttpLLMService>.Instance);
        });
        services.TryAddSingleton<ILLMService>(provider => provider.GetRequiredService<HttpLLMService>());
        services.TryAddSingleton<ILLMAgentService>(provider => provider.GetRequiredService<HttpLLMService>());
        services.TryAddSingleton<IAIChatService>(provider => provider.GetRequiredService<HttpLLMService>());
        services.AddScoped<ITicketAgentData, TicketAgentData>();
        services.AddScoped<IAgentRunStore, AgentRunStore>();
        services.AddScoped<ITTicketIntelligence.Application.Assistant.IAssistantStore, Persistence.Assistant.AssistantStore>();
        services.AddScoped<IMcpReadStore, McpReadStore>();
        services.AddScoped<IMcpAuditWriter, McpAuditWriter>();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(PostgresConnection.Create(configuration), postgres => postgres.CommandTimeout(30)));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<ITTicketIntelligence.Application.Security.IUserSessions, UserSessions>();
        services.AddScoped<ITTicketIntelligence.Application.Security.IAdminDirectory, AdminDirectory>();
        services.AddScoped<ITTicketIntelligence.Application.Tickets.Attachments.ITicketFormStore, Persistence.Attachments.TicketFormStore>();
        services.AddScoped<ITTicketIntelligence.Application.Tickets.Queries.ITicketWorkspaceReader, TicketWorkspaceReader>();
        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        services.AddSingleton<IKnowledgeTextExtractor, TextKnowledgeExtractor>();
        services.AddSingleton<IEmbeddingService>(provider => new OllamaEmbeddingService(
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), OllamaEmbeddingOptions.FromConfiguration(configuration),
            provider.GetService<ILogger<OllamaEmbeddingService>>() ?? NullLogger<OllamaEmbeddingService>.Instance));
        services.AddScoped<IVectorStore, PostgresVectorStore>();
        services.TryAddScoped<IKnowledgeAccessContext,DenyKnowledgeAccess>();
        services.AddScoped<IKnowledgeIndexQueue,KnowledgeIndexingService>();
        services.AddScoped<IKnowledgePermissions,KnowledgeIndexingService>();
        services.AddScoped<IKnowledgeChunkReader, KnowledgeChunkReader>();
        services.AddScoped<ITicketIntelligenceStore, TicketIntelligenceStore>();
        services.AddScoped<IAIConversationRepository, AIConversationRepository>();
        services.AddScoped<ITicketReferenceData, TicketReferenceData>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        return services;
    }
}
