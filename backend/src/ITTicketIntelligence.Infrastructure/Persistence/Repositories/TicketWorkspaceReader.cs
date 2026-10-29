using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Tickets.Queries;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class TicketWorkspaceReader(AppDbContext db) : ITicketWorkspaceReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public async Task<TicketWorkspaceDto?> ReadAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.AsNoTracking().Where(t => t.Id == ticketId)
            .Select(t => new { t.AssignedTeamId }).SingleOrDefaultAsync(cancellationToken);
        if (ticket is null) return null;
        var assigned = await db.Teams.AsNoTracking().Where(t => t.Id == ticket.AssignedTeamId)
            .Select(t => new TeamOptionDto(t.Id, t.Name, t.IsActive)).SingleOrDefaultAsync(cancellationToken);
        var teams = await db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new TeamOptionDto(t.Id, t.Name, t.IsActive)).Take(201).ToListAsync(cancellationToken);
        var stored = await db.AgentAnalyses.AsNoTracking().Where(a => a.TicketId == ticketId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken);
        SavedTicketAnalysis? analysis = null;
        if (stored is not null)
        {
            var result = JsonSerializer.Deserialize<TicketAgentResult>(stored.ResultJson, Json)
                ?? throw new InvalidOperationException("Invalid saved analysis.");
            // jsonb normalizes whitespace/property order; use the writer's DTO serialization.
            var canonical = JsonSerializer.Serialize(result, Json);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            if (!StringComparer.Ordinal.Equals(hash, stored.ResultHash)) throw new InvalidOperationException("Invalid saved analysis integrity.");
            if (result.RunId != stored.Id || result.Facts.Id != ticketId || result.TicketRevision != stored.TicketRevision)
                throw new InvalidOperationException("Invalid saved analysis provenance.");
            analysis = new(result, stored.CreatedAt);
        }
        return new(assigned, teams.Take(200).ToArray(), teams.Count > 200, analysis);
    }
}
