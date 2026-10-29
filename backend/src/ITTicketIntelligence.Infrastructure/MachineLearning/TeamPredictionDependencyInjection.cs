using System.Globalization;
using ITTicketIntelligence.Application.Intelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ITTicketIntelligence.Infrastructure.MachineLearning;

public static class TeamPredictionDependencyInjection
{
    public static IServiceCollection AddTicketTeamPrediction(this IServiceCollection services)
    {
        services.AddSingleton<TeamRecommendationPolicy>(provider =>
        {
            var text = provider.GetRequiredService<IConfiguration>()["MachineLearning:TeamMinimumConfidence"] ?? "0.8";
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) throw new ArgumentException("Invalid team confidence threshold configuration.");
            return new TeamRecommendationPolicy(threshold);
        });
        services.AddSingleton<ITicketTeamPredictor>(provider =>
        {
            var directory = provider.GetRequiredService<IConfiguration>()["MachineLearning:TeamModelDirectory"] ?? "Models/TicketTeam";
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            return new TicketTeamPredictor(Path.GetFullPath(directory, AppContext.BaseDirectory), provider.GetRequiredService<TeamRecommendationPolicy>());
        });
        return services;
    }
}
