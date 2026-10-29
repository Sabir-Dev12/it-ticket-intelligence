using ITTicketIntelligence.Application.Intelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class PriorityPredictionDependencyInjection
{
    public static IServiceCollection AddTicketPriorityPrediction(this IServiceCollection services)
    {
        services.AddSingleton<ITicketPriorityPredictor>(provider =>
        {
            var directory = provider.GetRequiredService<IConfiguration>()["MachineLearning:PriorityModelDirectory"] ?? "Models/TicketPriority";
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            return new TicketPriorityPredictor(Path.GetFullPath(directory, AppContext.BaseDirectory));
        });
        return services;
    }
}
