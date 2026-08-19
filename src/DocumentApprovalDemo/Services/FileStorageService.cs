using Microsoft.Extensions.Options;

namespace DocumentApprovalDemo.Services;

public sealed class FileStorageOptions
{
    public string RootPath { get; set; } = "App_Data/Uploads";
    public int MaximumFileSizeMb { get; set; } = 25;
}

public sealed record StoredFile(string StoredFileName, string OriginalFileName, string ContentType, long SizeBytes);

public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);
}

public sealed class LocalFileStorageService(IWebHostEnvironment environment, IOptions<FileStorageOptions> options) : IFileStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".txt"
    };

    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
    private readonly long maxBytes = options.Value.MaximumFileSizeMb * 1024L * 1024L;

    public async Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0) throw new InvalidOperationException("The supporting document is empty.");
        if (file.Length > maxBytes) throw new InvalidOperationException($"Files must be {maxBytes / 1024 / 1024} MB or smaller.");

        var originalName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalName);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Unsupported file type. Upload PDF, Office, image, or text files.");

        Directory.CreateDirectory(root);
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(root, storedName);
        try
        {
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await file.CopyToAsync(output, cancellationToken);
            await ValidateContentAsync(fullPath, extension, cancellationToken);
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }

        return new StoredFile(storedName, originalName, CanonicalContentType(extension), file.Length);
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(storedFileName);
        if (!string.Equals(safeName, storedFileName, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Stored file names cannot contain path segments.");
        var fullPath = Path.GetFullPath(Path.Combine(root, safeName));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException();
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(stream);
    }

    private static async Task ValidateContentAsync(string path, string extension, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var header = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
        var count = await stream.ReadAsync(header, cancellationToken);
        var bytes = header.AsSpan(0, count);
        var valid = extension.ToLowerInvariant() switch
        {
            ".pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
            ".png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            ".docx" or ".xlsx" => IsZip(bytes),
            ".doc" or ".xls" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }),
            ".txt" => !bytes.Contains((byte)0),
            _ => false
        };
        if (!valid) throw new InvalidOperationException("The file content does not match its supported file type.");
    }

    private static bool IsZip(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4b &&
        (bytes[2] is 0x03 or 0x05 or 0x07) && (bytes[3] is 0x04 or 0x06 or 0x08);

    private static string CanonicalContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}
