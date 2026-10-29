using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ITTicketIntelligence.Infrastructure.Persistence;

internal static class PostgresConnection
{
    public static string Create(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:Postgres in appsettings or environment variables.");
        }

        var connection = new NpgsqlConnectionStringBuilder(configured);
        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            connection.Password = configuration["Database:Password"];
        }

        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Set Database__Password or provide a complete ConnectionStrings__Postgres environment variable.");
        }

        connection.IncludeErrorDetail = false;
        return connection.ConnectionString;
    }
}
