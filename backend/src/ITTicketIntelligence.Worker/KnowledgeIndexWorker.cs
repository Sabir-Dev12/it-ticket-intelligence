using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ITTicketIntelligence.Worker;
public sealed class KnowledgeIndexWorker(IServiceScopeFactory scopes,ILogger<KnowledgeIndexWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { using var scope=scopes.CreateScope();if(await scope.ServiceProvider.GetRequiredService<IKnowledgeIndexQueue>().ProcessNextAsync(stoppingToken))continue; }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Knowledge worker unavailable: {ErrorType}",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);
        }
    }
}
