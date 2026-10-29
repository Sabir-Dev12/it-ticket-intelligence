using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Tickets.Attachments;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Tickets;

[Collection("Postgres")]
public sealed class TicketFormApiTests(PostgresFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private WebApplicationFactory<Api.Program> Host() => new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b =>
    { b.UseEnvironment("Testing"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.ConnectionString })); b.ConfigureLogging(l => l.ClearProviders()); });
    private async Task<(Guid user, Guid team)> Seed()
    {
        await using var db = fixture.CreateContext(); var now = DateTimeOffset.UtcNow.AddMinutes(-1); var user = Guid.NewGuid(); var team = Guid.NewGuid();
        db.Users.Add(new User(user, "Form test", EmailAddress.Create(user.ToString("N") + "@example.test"), UserRole.Agent, now)); db.Teams.Add(new Team(team, "Form " + team.ToString("N"), now)); await db.SaveChangesAsync(); return (user, team);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    { Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid ticket, int revision, Guid id, string name = "log.txt", string type = "text/plain", byte[]? bytes = null)
    {
        var form = new MultipartFormDataContent(); form.Add(new StringContent(id.ToString()), "attachmentId"); form.Add(new StringContent(revision.ToString()), "revision");
        var file = new ByteArrayContent(bytes ?? "Observed SQL timeout"u8.ToArray()); file.Headers.ContentType = new MediaTypeHeaderValue(type); form.Add(file, "file", name);
        return Send();
        async Task<HttpResponseMessage> Send() { using (form) return await client.PostAsync($"/api/tickets/{ticket}/attachments", form); }
    }
    [Fact]
    public async Task CompleteFormSavesClassificationAndAssignmentAtomicallyWithLegacyCompatibility()
    {
        var seed = await Seed(); await using var host = Host(); using var client = host.CreateClient();
        var options = await client.GetFromJsonAsync<TicketFormOptions>("/api/tickets/form-options", Json); Assert.Contains(options!.Teams, t => t.Id == seed.team);
        var created = await Read<TicketDto>(await client.PostAsJsonAsync("/api/tickets", new { title = "SQL issue", description = "Connection times out", createdById = seed.user, category = "Database", priority = "High", assignedTeamId = seed.team }), HttpStatusCode.Created);
        Assert.Equal(TicketCategory.Database, created.Category); Assert.Equal(seed.team, created.AssignedTeamId);
        var failed = await client.PutAsJsonAsync($"/api/tickets/{created.Id}", new { title = "Must not be saved", description = "New description", category = "Network", priority = "Low", revision = created.Revision, assignment = new { teamId = Guid.NewGuid() } }); Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
        var unchanged = await client.GetFromJsonAsync<TicketDto>($"/api/tickets/{created.Id}", Json); Assert.Equal(created.Title, unchanged!.Title); Assert.Equal(created.Revision, unchanged.Revision);
        var legacy = await Read<TicketDto>(await client.PutAsJsonAsync($"/api/tickets/{created.Id}", new { title = "Updated issue", description = "Additional observation", category = "Database", priority = "High", revision = created.Revision })); Assert.Equal(seed.team, legacy.AssignedTeamId);
        var clear = await Read<TicketDto>(await client.PutAsJsonAsync($"/api/tickets/{created.Id}", new { title = legacy.Title, description = legacy.Description, category = "Database", priority = "Medium", revision = legacy.Revision, assignment = new { teamId = (Guid?)null } })); Assert.Null(clear.AssignedTeamId);
        var stale = await client.PutAsJsonAsync($"/api/tickets/{created.Id}", new { title = "Stale write", description = "No overwrite", revision = created.Revision }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var partial = await client.PostAsJsonAsync("/api/tickets", new { title = "Invalid", description = "Invalid pair", createdById = seed.user, category = "Hardware" }); Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        var malformed = await client.PutAsJsonAsync($"/api/tickets/{created.Id}", new { title = "Invalid", description = "No missing assignment property", revision = clear.Revision, assignment = new { } }); Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }
    [Fact]
    public async Task AttachmentsRoundTripAndRetryWithoutDuplicatingOrCrossingTicketBoundaries()
    {
        var seed = await Seed(); await using var host = Host(); using var client = host.CreateClient();
        var ticket = await Read<TicketDto>(await client.PostAsJsonAsync("/api/tickets", new { title = "Observed failure", description = "Attach a diagnostic log", createdById = seed.user }), HttpStatusCode.Created);
        var id = Guid.NewGuid(); var saved = await Read<AttachmentSaved>(await Upload(client, ticket.Id, ticket.Revision, id), HttpStatusCode.Created); Assert.True(saved.Created);
        var replay = await Read<AttachmentSaved>(await Upload(client, ticket.Id, ticket.Revision, id), HttpStatusCode.Created); Assert.False(replay.Created); Assert.Equal(saved.Revision, replay.Revision);
        var list = await client.GetFromJsonAsync<AttachmentList>($"/api/tickets/{ticket.Id}/attachments", Json); Assert.Single(list!.Items);
        using var content = await client.GetAsync($"/api/tickets/{ticket.Id}/attachments/{id}"); Assert.Equal("Observed SQL timeout", await content.Content.ReadAsStringAsync()); Assert.Equal("attachment", content.Content.Headers.ContentDisposition!.DispositionType); Assert.Contains("nosniff", content.Headers.GetValues("X-Content-Type-Options"));
        using var crossed = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}/attachments/{id}"); Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        using var stale = await client.DeleteAsync($"/api/tickets/{ticket.Id}/attachments/{id}?revision={ticket.Revision}"); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var removed = await client.DeleteAsync($"/api/tickets/{ticket.Id}/attachments/{id}?revision={saved.Revision}"); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<AttachmentList>($"/api/tickets/{ticket.Id}/attachments", Json))!.Items);
        await using var db = fixture.CreateContext(); Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == ticket.Id && a.Action.StartsWith("TicketAttachment")));
    }
    [Fact]
    public async Task ServerRejectsSpoofedOversizedAndExcessFilesAndInactiveTickets()
    {
        var seed = await Seed(); await using var host = Host(); using var client = host.CreateClient();
        var ticket = await Read<TicketDto>(await client.PostAsJsonAsync("/api/tickets", new { title = "Validation test", description = "Validate incoming attachments", createdById = seed.user }), HttpStatusCode.Created);
        using var spoof = await Upload(client, ticket.Id, ticket.Revision, Guid.NewGuid(), "fake.png", "image/png"); Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var oversized = await Upload(client, ticket.Id, ticket.Revision, Guid.NewGuid(), bytes: new byte[AttachmentPolicy.MaximumBytes + 1]); Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        var revision = ticket.Revision;
        for (var i = 0; i < 5; i++) revision = (await Read<AttachmentSaved>(await Upload(client, ticket.Id, revision, Guid.NewGuid()), HttpStatusCode.Created)).Revision;
        using var excess = await Upload(client, ticket.Id, revision, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, excess.StatusCode);
        using var archived = await client.DeleteAsync($"/api/tickets/{ticket.Id}?revision={revision}"); Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        using var denied = await Upload(client, ticket.Id, revision + 1, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
    }
}
