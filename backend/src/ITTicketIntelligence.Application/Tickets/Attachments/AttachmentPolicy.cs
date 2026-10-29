using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ITTicketIntelligence.Application.Common;

namespace ITTicketIntelligence.Application.Tickets.Attachments;

public static class AttachmentPolicy
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const int MaximumCount = 5;
    public static ValidatedAttachment Validate(string? name, string? contentType, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.Any(c => char.IsControl(c) || "/\\:<>\"|?*".Contains(c)) || name is "." or "..") Invalid("Use a plain filename of at most 180 characters.");
        if (data.Length is 0 or > MaximumBytes) Invalid("Each attachment must contain 1 byte to 5 MiB.");
        var extension = Path.GetExtension(name!).ToLowerInvariant();
        var expected = extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", ".txt" => "text/plain", _ => "" };
        if (expected.Length == 0 || contentType?.Split(';')[0].Trim().ToLowerInvariant() != expected) Invalid("Only PNG, JPEG, PDF and UTF-8 TXT files with matching content types are accepted.");
        var bytes = data.AsSpan();
        var valid = expected switch
        {
            "image/png" => bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8) && ValidDimensions(bytes),
            "image/jpeg" => bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217,
            "application/pdf" => bytes.StartsWith("%PDF-"u8) && bytes[Math.Max(0, bytes.Length - 1024)..].IndexOf("%%EOF"u8) >= 0,
            "text/plain" => ValidText(data),
            _ => false
        };
        if (!valid) Invalid("File contents do not match the allowed format. Export the original file again.");
        return new(name!.Trim(), expected, data, Convert.ToHexStringLower(SHA256.HashData(data)));
    }
    private static bool ValidDimensions(ReadOnlySpan<byte> bytes)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)); var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return width is > 0 and <= 8192 && height is > 0 and <= 8192 && (ulong)width * height <= 16000000;
    }
    private static bool ValidText(byte[] data)
    {
        try { return !new UTF8Encoding(false, true).GetString(data).Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')); }
        catch (DecoderFallbackException) { return false; }
    }
    private static void Invalid(string message) => throw new RequestValidationException(new Dictionary<string, string[]> { ["attachments"] = [message] });
}
