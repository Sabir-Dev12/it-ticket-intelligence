using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Attachments;
using Microsoft.AspNetCore.Mvc;

namespace ITTicketIntelligence.Api.Controllers;

[ApiController, Route("api/tickets")]
public sealed class TicketFilesController(ITicketFormStore store, TicketFileService files) : ControllerBase
{
    [HttpGet("form-options")]
    public async Task<ActionResult<TicketFormOptions>> Options(CancellationToken ct) => Ok(await store.OptionsAsync(ct));
    [HttpGet("{ticketId:guid}/attachments")]
    public async Task<ActionResult<AttachmentList>> List(Guid ticketId, CancellationToken ct) => Ok(await store.ListAsync(ticketId, ct));
    [HttpGet("{ticketId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Read(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        var result = await store.ReadAsync(ticketId, attachmentId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.CacheControl = "no-store";
        return File(result.Data, result.Attachment.ContentType, result.Attachment.FileName);
    }
    [HttpPost("{ticketId:guid}/attachments")]
    [RequestSizeLimit(AttachmentPolicy.MaximumBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = AttachmentPolicy.MaximumBytes + 32768)]
    [ProducesResponseType(typeof(AttachmentSaved), 201)]
    public async Task<ActionResult<AttachmentSaved>> Upload(Guid ticketId, [FromForm] Guid attachmentId, [FromForm] int revision, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > AttachmentPolicy.MaximumBytes) throw new RequestValidationException(new Dictionary<string, string[]> { ["attachments"] = ["Each attachment must contain 1 byte to 5 MiB."] });
        using var buffer = new MemoryStream();
        await using var input = file.OpenReadStream();
        var chunk = new byte[8192]; int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > AttachmentPolicy.MaximumBytes) return StatusCode(413);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var result = await files.UploadAsync(ticketId, attachmentId, revision, file.FileName, file.ContentType, buffer.ToArray(), ct);
        return CreatedAtAction(nameof(Read), new { ticketId, attachmentId }, result);
    }
    [HttpDelete("{ticketId:guid}/attachments/{attachmentId:guid}")]
    public async Task<ActionResult<object>> Delete(Guid ticketId, Guid attachmentId, [FromQuery] int revision, CancellationToken ct) =>
        Ok(new { revision = await files.DeleteAsync(ticketId, attachmentId, revision, ct) });
}
