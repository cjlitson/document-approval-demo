using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public enum FilePreviewKind { Pdf, Image, Text, Unavailable }

public sealed record FilePreviewCapability(
    bool CanPreview,
    FilePreviewKind Kind,
    string ContentType,
    string? UnavailableReason = null);

public interface IFilePreviewService
{
    FilePreviewCapability GetCapability(RequestAttachment attachment);
    Task<FilePreviewCapability> InspectAsync(RequestAttachment attachment, Stream content, CancellationToken cancellationToken = default);
    Task<string> GetDownloadContentTypeAsync(RequestAttachment attachment, Stream content, CancellationToken cancellationToken = default);
}

public sealed class FilePreviewService : IFilePreviewService
{
    public FilePreviewCapability GetCapability(RequestAttachment attachment) =>
        Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant() switch
        {
            ".pdf" => new(true, FilePreviewKind.Pdf, "application/pdf"),
            ".png" => new(true, FilePreviewKind.Image, "image/png"),
            ".jpg" or ".jpeg" => new(true, FilePreviewKind.Image, "image/jpeg"),
            ".txt" => new(true, FilePreviewKind.Text, "text/plain; charset=utf-8"),
            ".doc" or ".docx" or ".xls" or ".xlsx" => new(
                false,
                FilePreviewKind.Unavailable,
                "application/octet-stream",
                "Preview unavailable for this Office file type. Download the original to open it in an approved desktop application."),
            _ => new(false, FilePreviewKind.Unavailable, "application/octet-stream", "Preview unavailable for this file type.")
        };

    public async Task<FilePreviewCapability> InspectAsync(
        RequestAttachment attachment,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var expected = GetCapability(attachment);
        if (!expected.CanPreview) return expected;
        var header = await ReadHeaderAsync(content, 4096, cancellationToken);
        Reset(content);
        var valid = expected.Kind switch
        {
            FilePreviewKind.Pdf => header.Length >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            FilePreviewKind.Image when expected.ContentType == "image/png" =>
                header.Length >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            FilePreviewKind.Image =>
                header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            FilePreviewKind.Text => !header.Contains((byte)0),
            _ => false
        };
        return valid
            ? expected
            : new(false, FilePreviewKind.Unavailable, "application/octet-stream", "Preview blocked because the file content does not match its declared type.");
    }

    public async Task<string> GetDownloadContentTypeAsync(
        RequestAttachment attachment,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var preview = await InspectAsync(attachment, content, cancellationToken);
        if (preview.CanPreview) return preview.ContentType;

        var extension = Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant();
        var header = await ReadHeaderAsync(content, 16, cancellationToken);
        Reset(content);
        var isZip = header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4b &&
                    (header[2] is 0x03 or 0x05 or 0x07) && (header[3] is 0x04 or 0x06 or 0x08);
        var isCompound = header.Length >= 8 &&
                         header.AsSpan(0, 8).SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 });
        return extension switch
        {
            ".docx" when isZip => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" when isZip => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".doc" when isCompound => "application/msword",
            ".xls" when isCompound => "application/vnd.ms-excel",
            _ => "application/octet-stream"
        };
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        Reset(stream);
        var buffer = new byte[maximum];
        var count = await stream.ReadAsync(buffer.AsMemory(0, maximum), cancellationToken);
        return count == maximum ? buffer : buffer[..count];
    }

    private static void Reset(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
    }
}
