using ITTicketIntelligence.Domain.Tickets;

namespace ITTicketIntelligence.UnitTests.Domain;

internal static class DomainTestData
{
    public static DateTimeOffset Now => new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    public static Ticket Ticket() => new(Guid.NewGuid(), "IT-1045", "SQL Server connection failed",
        "The production application cannot connect to SQL Server.", Guid.NewGuid(), Now);
}
