using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Attachments;
using ITTicketIntelligence.Application.Tickets.Queries;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence.Attachments;

internal sealed class TicketFormStore(AppDbContext db, TimeProvider clock) : ITicketFormStore
{
    private static AttachmentDto Dto(AttachmentRecord file) => new(file.Id, file.TicketId, file.FileName, file.ContentType, file.Length, file.Sha256, file.CreatedAt);
    public async Task<TicketFormOptions> OptionsAsync(CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new TeamOptionDto(t.Id, t.Name, t.IsActive)).Take(201).ToListAsync(ct);
        var people = await db.Users.AsNoTracking().Where(u => u.IsActive && (u.Role == ITTicketIntelligence.Domain.Enums.UserRole.Agent || u.Role == ITTicketIntelligence.Domain.Enums.UserRole.Support))
            .OrderBy(u => u.Name).ThenBy(u => u.Id).Select(u => new AssigneeOptionDto(u.Id, u.Name, u.Role.ToString())).Take(201).ToListAsync(ct);
        return new(teams.Take(200).ToArray(), teams.Count > 200, people.Take(200).ToArray(), people.Count > 200);
    }
    public async Task<AttachmentList> ListAsync(Guid ticketId, CancellationToken ct)
    {
        var revision = await db.Tickets.Where(t => t.Id == ticketId).Select(t => (int?)t.Revision).SingleOrDefaultAsync(ct) ?? throw new ResourceNotFoundException("Ticket");
        var files = await db.TicketAttachments.AsNoTracking().Where(a => a.TicketId == ticketId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new AttachmentDto(a.Id, a.TicketId, a.FileName, a.ContentType, a.Length, a.Sha256, a.CreatedAt)).ToListAsync(ct);
        return new(files, revision);
    }
    public async Task<AttachmentContent> ReadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        var file = await db.TicketAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId, ct) ?? throw new ResourceNotFoundException("Attachment");
        return new(Dto(file), file.Data);
    }
    private async Task<Ticket> Lock(Guid id, CancellationToken ct) =>
        await db.Tickets.FromSqlInterpolated($"SELECT *, xmin FROM app.tickets WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new ResourceNotFoundException("Ticket");
    public async Task<AttachmentSaved> SaveAsync(Guid ticketId, Guid id, int revision, ValidatedAttachment file, CancellationToken ct)
    {
        if (id == Guid.Empty || revision < 1) throw new RequestValidationException(new Dictionary<string, string[]> { ["attachments"] = ["A valid attachment ID and positive revision are required."] });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await Lock(ticketId, ct);
        var existing = await db.TicketAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (existing is not null)
        {
            if (existing.TicketId != ticketId || existing.Sha256 != file.Sha256 || existing.FileName != file.FileName || existing.ContentType != file.ContentType) throw new ConflictException("This attachment identifier is already in use.");
            return new(Dto(existing), ticket.Revision, false);
        }
        if (ticket.Revision != revision) throw new ConflictException("The ticket changed. Refresh before uploading.");
        if (await db.TicketAttachments.CountAsync(a => a.TicketId == ticketId, ct) >= AttachmentPolicy.MaximumCount) throw new ConflictException("A ticket can contain at most five attachments.");
        var now = clock.GetUtcNow(); ticket.RecordAttachmentChange(now);
        var record = new AttachmentRecord { Id = id, TicketId = ticketId, FileName = file.FileName, ContentType = file.ContentType, Data = file.Data, Length = file.Data.Length, Sha256 = file.Sha256, CreatedAt = now };
        db.TicketAttachments.Add(record);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, "TicketAttachmentAdded", "Ticket", ticketId, now, $"Attachment {id}; SHA256 {file.Sha256}; {file.Data.Length} bytes."));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(Dto(record), ticket.Revision);
    }
    public async Task<int> DeleteAsync(Guid ticketId, Guid attachmentId, int revision, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await Lock(ticketId, ct);
        if (ticket.Revision != revision) throw new ConflictException("The ticket changed. Refresh before removing an attachment.");
        var file = await db.TicketAttachments.SingleOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId, ct) ?? throw new ResourceNotFoundException("Attachment");
        var now = clock.GetUtcNow(); ticket.RecordAttachmentChange(now); db.TicketAttachments.Remove(file);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), null, "TicketAttachmentRemoved", "Ticket", ticketId, now, $"Attachment {attachmentId}; SHA256 {file.Sha256}."));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ticket.Revision;
    }
}
