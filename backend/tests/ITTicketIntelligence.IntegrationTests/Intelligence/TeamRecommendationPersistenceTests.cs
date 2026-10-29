using System.Net;
using System.Net.Http.Json;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Intelligence;

[Collection("Postgres")]
public sealed class TeamRecommendationPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task EvenAnAcceptedRecommendationDoesNotAssignOrModifyATicket()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        int auditCount, predictionCount;
        await using (var db = fixture.CreateContext())
        {
            var user = new User(Guid.NewGuid(), "Routing test", EmailAddress.Create(Guid.NewGuid().ToString("N") + "@example.com"), UserRole.User, now);
            db.Users.Add(user);
            db.Tickets.Add(new Ticket(id, "TEAM-" + id.ToString("N")[..20], "SQL lock waits", "Writes cannot proceed because a database transaction holds locks.", user.Id, now));
            await db.SaveChangesAsync();
            auditCount = await db.AuditLogs.CountAsync();
            predictionCount = await db.TicketPredictions.CountAsync();
        }
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketTeamPredictor>();
                services.AddSingleton<ITicketTeamPredictor, AcceptedPredictor>();
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/predictions/team", new { title = "SQL lock waits", description = "Writes cannot proceed because a database transaction holds locks." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TeamRecommendation>();
        Assert.NotNull(result);
        Assert.False(result.NeedsHumanReview);
        Assert.Equal("Database", result.Team);
        await using var verify = fixture.CreateContext();
        var ticket = await verify.Tickets.SingleAsync(row => row.Id == id);
        Assert.Null(ticket.AssignedTeamId);
        Assert.Equal(1, ticket.Revision);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(auditCount, await verify.AuditLogs.CountAsync());
        Assert.Equal(predictionCount, await verify.TicketPredictions.CountAsync());
    }

    private sealed class AcceptedPredictor : ITicketTeamPredictor
    {
        public Task<TeamRecommendation> PredictAsync(TicketTeamInput input, CancellationToken cancellationToken = default) => Task.FromResult(new TeamRecommendationPolicy().Apply("Database", 0.99, "accepted-test"));
    }
}
