using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ITTicketIntelligence.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new InvalidOperationException("Run EF commands from the project repository.");
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root.FullName, "backend", "src", "ITTicketIntelligence.Api", "appsettings.json"))
            .AddEnvironmentVariables()
            .Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresConnection.Create(configuration), postgres => postgres.CommandTimeout(30))
            .Options;
        return new AppDbContext(options);
    }
}
