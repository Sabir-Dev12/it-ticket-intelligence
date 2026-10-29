using ITTicketIntelligence.Application.Intelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class DependencyInjection
{
    public static IServiceCollection AddTicketCategoryPrediction(this IServiceCollection services)
    {
        services.AddSingleton<ITicketCategoryPredictor>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var modelDirectory = configuration["MachineLearning:CategoryModelDirectory"] ?? "Models/TicketCategory";
            ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
            return new TicketCategoryPredictor(Path.GetFullPath(modelDirectory, AppContext.BaseDirectory));
        });
        return services;
    }
}
