using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Persistence;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string databaseName = "itt_tests_" + Guid.NewGuid().ToString("N");
    private string administrativeConnection = "";
    private bool created;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=55432;Database=it_ticket_intelligence;Username=ticket_app;Timeout=10";
        var connection = new NpgsqlConnectionStringBuilder(configured);
        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            connection.Password = Environment.GetEnvironmentVariable("Database__Password");
        }
        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Persistence tests require PostgreSQL and the local environment. Import scripts/Import-LocalEnvironment.ps1 before running dotnet test.");
        }
        connection.IncludeErrorDetail = false;
        connection.Database = "postgres";
        administrativeConnection = connection.ConnectionString;
        await using var admin = new NpgsqlConnection(administrativeConnection);
        await admin.OpenAsync();
        // The name is generated solely from a GUID, never from user input.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await command.ExecuteNonQueryAsync();
        created = true;
        connection.Database = databaseName;
        ConnectionString = connection.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (!created) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(administrativeConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}
