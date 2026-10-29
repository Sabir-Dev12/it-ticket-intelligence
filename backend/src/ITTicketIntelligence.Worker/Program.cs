using ITTicketIntelligence.Application;
using ITTicketIntelligence.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace ITTicketIntelligence.Worker;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(context.Configuration);
                services.AddHostedService<KnowledgeIndexWorker>();
            });
    }
}
