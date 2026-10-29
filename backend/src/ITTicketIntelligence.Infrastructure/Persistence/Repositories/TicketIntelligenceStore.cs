using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Repositories;

internal sealed class TicketIntelligenceStore(AppDbContext db) : ITicketIntelligenceStore
{
    public Task<TicketAnalysisInput?> GetInputAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        db.Tickets.AsNoTracking().Where(ticket => ticket.Id == ticketId)
            .Select(ticket => new TicketAnalysisInput(ticket.Id, ticket.Title, ticket.Description,
                ticket.Revision, ticket.Status, ticket.IsArchived)).SingleOrDefaultAsync(cancellationToken);

    public async Task SaveAsync(Guid ticketId, int inputRevision, IReadOnlyList<TicketPrediction> predictions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        if (predictions.Count != 3 || predictions.Select(p => p.Type).Distinct().Count() != 3 ||
            predictions.Any(p => p.TicketId != ticketId || p.InputRevision != inputRevision || p.AnalysisId is null) ||
            predictions.Select(p => p.AnalysisId).Distinct().Count() != 1)
            throw new ArgumentException("An analysis must contain three predictions for one ticket revision and analysis ID.", nameof(predictions));

        // Keep inference outside the transaction. The row lock closes the check/insert race with ticket edits.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT *, xmin FROM app.tickets WHERE id = {ticketId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Ticket");
        if (ticket.Revision != inputRevision)
            throw new ConflictException("The ticket changed during analysis. Retry using the current ticket.");
        if (ticket.IsArchived || ticket.Status is not (TicketStatus.Open or TicketStatus.InProgress))
            throw new ConflictException("Only an open or in-progress, unarchived ticket can be analyzed.");

        foreach (var prediction in predictions) ticket.RecordPrediction(prediction);
        db.TicketPredictions.AddRange(predictions);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
