using ITTicketIntelligence.Domain.Auditing;

namespace ITTicketIntelligence.Application.Abstractions.Persistence;

public interface IAuditLogWriter
{
    void Add(AuditLog audit);
}
