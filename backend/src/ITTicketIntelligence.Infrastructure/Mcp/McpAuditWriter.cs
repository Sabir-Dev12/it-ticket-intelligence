using System.Text.Json;
using ITTicketIntelligence.Application.Mcp.Contracts;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Mcp;

internal sealed class McpAuditWriter(DbContextOptions<AppDbContext> options) : IMcpAuditWriter
{
    public async Task WriteAsync(McpAuditEntry entry, CancellationToken ct)
    {
        await using var db = new AppDbContext(options);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, "Mcp.Tool." + entry.Outcome, "McpToolCall", entry.CallId,
            entry.OccurredAt, JsonSerializer.Serialize(new { entry.Subject, entry.Tool, entry.ArgumentsHash, entry.OutputHash }, McpToolCatalog.Json), entry.CallId));
        await db.SaveChangesAsync(ct);
    }
}
