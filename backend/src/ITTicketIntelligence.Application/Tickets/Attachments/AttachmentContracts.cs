using ITTicketIntelligence.Application.Tickets.Queries;

namespace ITTicketIntelligence.Application.Tickets.Attachments;

public sealed record AttachmentDto(Guid Id, Guid TicketId, string FileName, string ContentType, int Length, string Sha256, DateTimeOffset CreatedAt);
public sealed record AttachmentList(IReadOnlyList<AttachmentDto> Items, int Revision);
public sealed record AttachmentSaved(AttachmentDto Attachment, int Revision, bool Created = true);
public sealed record AttachmentContent(AttachmentDto Attachment, byte[] Data);
public sealed record AssigneeOptionDto(Guid Id, string Name, string Role);
public sealed record TicketFormOptions(IReadOnlyList<TeamOptionDto> Teams, bool TeamsTruncated, IReadOnlyList<AssigneeOptionDto>? Assignees = null, bool AssigneesTruncated = false);
public interface ITicketFormStore
{
    Task<TicketFormOptions> OptionsAsync(CancellationToken ct);
    Task<AttachmentList> ListAsync(Guid ticketId, CancellationToken ct);
    Task<AttachmentContent> ReadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct);
    Task<AttachmentSaved> SaveAsync(Guid ticketId, Guid id, int revision, ValidatedAttachment file, CancellationToken ct);
    Task<int> DeleteAsync(Guid ticketId, Guid attachmentId, int revision, CancellationToken ct);
}
public sealed record ValidatedAttachment(string FileName, string ContentType, byte[] Data, string Sha256);

public sealed class TicketFileService(ITicketFormStore store, Realtime.TicketEventNotifier notifications, TimeProvider clock)
{
    public async Task<AttachmentSaved> UploadAsync(Guid ticketId, Guid id, int revision, string name, string type, byte[] bytes, CancellationToken ct)
    {
        var result = await store.SaveAsync(ticketId, id, revision, AttachmentPolicy.Validate(name, type, bytes), ct);
        if (result.Created) await notifications.PublishAsync(Realtime.TicketEventName.TicketUpdated, ticketId, result.Revision, result.Attachment.CreatedAt);
        return result;
    }
    public async Task<int> DeleteAsync(Guid ticketId, Guid id, int revision, CancellationToken ct)
    {
        var result = await store.DeleteAsync(ticketId, id, revision, ct);
        await notifications.PublishAsync(Realtime.TicketEventName.TicketUpdated, ticketId, result, clock.GetUtcNow());
        return result;
    }
}
