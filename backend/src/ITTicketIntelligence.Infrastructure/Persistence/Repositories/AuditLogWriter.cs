using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Domain.Auditing;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class AuditLogWriter(AppDbContext db) : IAuditLogWriter
{
    public void Add(AuditLog audit) => db.AuditLogs.Add(audit);
}
