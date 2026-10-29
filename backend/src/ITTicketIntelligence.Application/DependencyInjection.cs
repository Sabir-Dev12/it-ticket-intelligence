using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Commands;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Mcp;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Application.Realtime;

namespace ITTicketIntelligence.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<Tickets.Attachments.TicketFileService>();
        services.TryAddSingleton<ITicketEventSink, NullTicketEventSink>();
        services.AddScoped<TicketEventNotifier>(p => new TicketEventNotifier(p.GetRequiredService<ITicketEventSink>(),
            p.GetService<ILogger<TicketEventNotifier>>() ?? NullLogger<TicketEventNotifier>.Instance));
        services.AddScoped<IMcpToolService, McpToolService>();
        services.AddScoped<ITicketAgentTools, McpAgentTools>();
        services.AddScoped<ITicketAgent, TicketAgent>();
        services.AddScoped<ITTicketIntelligence.Application.Agents.IConversationTicketAgent, TicketAgent>();
        services.AddScoped<ITTicketIntelligence.Application.Assistant.IAssistantService, ITTicketIntelligence.Application.Assistant.AssistantService>();
        services.AddScoped<IQueryHandler<GetTicketWorkspaceQuery, TicketWorkspaceDto>, GetTicketWorkspaceHandler>();
        services.AddScoped<IRagIndexer>(provider => new RagIndexer(provider.GetRequiredService<IKnowledgeRepository>(),
            provider.GetRequiredService<IEmbeddingService>(), provider.GetRequiredService<IVectorStore>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<ILogger<RagIndexer>>() ?? NullLogger<RagIndexer>.Instance));
        services.AddScoped<IRagRetriever>(provider => new RagRetriever(provider.GetRequiredService<IEmbeddingService>(),
            provider.GetRequiredService<IVectorStore>(), provider.GetRequiredService<IKnowledgeChunkReader>(),
            provider.GetService<ILogger<RagRetriever>>() ?? NullLogger<RagRetriever>.Instance));
        services.AddScoped<IKnowledgeDocumentProcessor, KnowledgeDocumentProcessor>();
        services.AddScoped<ICommandHandler<CreateKnowledgeCommand, KnowledgeDocumentDto>, KnowledgeCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateKnowledgeCommand, KnowledgeDocumentDto>, KnowledgeCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteKnowledgeCommand, bool>, KnowledgeCommandHandler>();
        services.AddScoped<IQueryHandler<GetKnowledgeQuery, KnowledgeDocumentDto>, KnowledgeQueryHandler>();
        services.AddScoped<IQueryHandler<GetKnowledgePageQuery, KnowledgePage>, KnowledgeQueryHandler>();
        services.TryAddSingleton(new IntelligenceReviewPolicy());
        services.AddScoped<ITicketIntelligencePipeline, TicketIntelligencePipeline>();
        services.AddScoped<IQueryHandler<PredictTicketCategoryQuery, TicketCategoryPrediction>, PredictTicketCategoryHandler>();
        services.AddScoped<IQueryHandler<PredictTicketPriorityQuery, TicketPriorityPrediction>, PredictTicketPriorityHandler>();
        services.AddScoped<IQueryHandler<PredictTicketTeamQuery, TeamRecommendation>, PredictTicketTeamHandler>();
        services.AddScoped<TicketCommandHandler>();
        services.AddScoped<ICommandHandler<CreateTicketCommand,TicketDto>>(p=>new CreateAnalyzedTicketHandler(p.GetRequiredService<TicketCommandHandler>(),p.GetRequiredService<ITicketIntelligencePipeline>(),p.GetRequiredService<IQueryHandler<GetTicketQuery,TicketDto>>(),p.GetService<ILogger<CreateAnalyzedTicketHandler>>()??NullLogger<CreateAnalyzedTicketHandler>.Instance));
        services.AddScoped<ICommandHandler<UpdateTicketCommand, TicketDto>, TicketCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeTicketStatusCommand, TicketDto>, TicketCommandHandler>();
        services.AddScoped<ICommandHandler<AssignTicketTeamCommand, TicketDto>, TicketCommandHandler>();
        services.AddScoped<ICommandHandler<AddTicketCommentCommand, AddCommentResult>, TicketCommandHandler>();
        services.AddScoped<ICommandHandler<ArchiveTicketCommand, TicketDto>, TicketCommandHandler>();
        services.AddScoped<ICommandHandler<RestoreTicketCommand, TicketDto>, TicketCommandHandler>();
        services.AddScoped<IQueryHandler<GetTicketQuery, TicketDto>, TicketQueryHandler>();
        services.AddScoped<IQueryHandler<GetTicketsQuery, PagedResult<TicketSummaryDto>>, TicketQueryHandler>();
        services.AddScoped<LanguageModels.AIChatConversations>();
        services.AddScoped<SemanticSearchService>();
        services.AddScoped<Assistant.KnowledgeArticleAssistant>();
        return services;
    }
}
