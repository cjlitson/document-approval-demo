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
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await file.CopyToAsync(output, cancellationToken);

        return new StoredFile(storedName, originalName, file.ContentType ?? "application/octet-stream", file.Length);
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.GetFullPath(Path.Combine(root, safeName));
        if (!fullPath.StartsWith(root, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(stream);
    }
}

