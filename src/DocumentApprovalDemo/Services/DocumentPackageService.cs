using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public sealed record PackageAttachmentManifest(
    string OriginalFilename,
    string PackagedFilename,
    int Revision,
    string ContentType,
    long Size,
    string Sha256);

public sealed record DocumentPackageManifest(
    Guid RequestId,
    string RequestNumber,
    string DocumentType,
    string FinalStatus,
    int Revision,
    int? RouteVersion,
    string Requester,
    DateTimeOffset GeneratedAtUtc,
    string ApprovalRecord,
    IReadOnlyList<PackageAttachmentManifest> Attachments);

public interface IDocumentPackageService
{
    Task<byte[]> BuildAsync(
        ApprovalRequest request,
        IReadOnlyList<AuditEvent>? history = null,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentPackageService(
    IApprovalRecordService approvalRecord,
    IFileStorageService fileStorage,
    IFilePreviewService filePreview) : IDocumentPackageService
{
    public async Task<byte[]> BuildAsync(
        ApprovalRequest request,
        IReadOnlyList<AuditEvent>? history = null,
        CancellationToken cancellationToken = default)
    {
        if (request.Status != RequestStatus.Approved)
            throw new InvalidOperationException("The document package is available only after final approval.");

        var generatedAt = DateTimeOffset.UtcNow;
        var approvalFileName = $"{SafeName(request.RequestNumber)}-Approval-Record.pdf";
        var manifests = new List<PackageAttachmentManifest>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var output = new MemoryStream();

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var approvalEntry = archive.CreateEntry(approvalFileName, CompressionLevel.Optimal);
            approvalEntry.LastWriteTime = generatedAt;
            await using (var destination = approvalEntry.Open())
            {
                var approvalBytes = approvalRecord.Build(request, history);
                await destination.WriteAsync(approvalBytes, cancellationToken);
            }

            foreach (var attachment in request.Attachments.OrderBy(x => x.RevisionNumber).ThenBy(x => x.OriginalFileName))
            {
                var directory = $"Attachments/Revision-{attachment.RevisionNumber:00}";
                var filename = UniqueName(SafeName(attachment.OriginalFileName), directory, usedPaths);
                var packagedPath = $"{directory}/{filename}";
                await using var source = await fileStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
                var contentType = await filePreview.GetDownloadContentTypeAsync(attachment, source, cancellationToken);
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken);
                var bytes = buffer.ToArray();
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

                var entry = archive.CreateEntry(packagedPath, CompressionLevel.Optimal);
                entry.LastWriteTime = generatedAt;
                await using (var destination = entry.Open())
                    await destination.WriteAsync(bytes, cancellationToken);

                manifests.Add(new PackageAttachmentManifest(
                    attachment.OriginalFileName,
                    packagedPath,
                    attachment.RevisionNumber,
                    contentType,
                    bytes.LongLength,
                    hash));
            }

            var manifest = new DocumentPackageManifest(
                request.Id,
                request.RequestNumber,
                request.DocumentType.Name,
                request.Status.ToString(),
                request.CurrentRevisionNumber,
                request.RouteVersion?.VersionNumber,
                request.Requester.FullName,
                generatedAt,
                approvalFileName,
                manifests);
            var manifestEntry = archive.CreateEntry("Package-Manifest.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = generatedAt;
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(
                manifestStream,
                manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
                cancellationToken);
        }
        return output.ToArray();
    }

    private static string UniqueName(string filename, string directory, ISet<string> used)
    {
        var candidate = filename;
        var stem = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        var counter = 2;
        while (!used.Add($"{directory}/{candidate}"))
            candidate = $"{stem}-{counter++}{extension}";
        return candidate;
    }

    internal static string SafeName(string value)
    {
        var name = Path.GetFileName(value);
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        name = new string(name.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "document";
        return name.Length <= 180 ? name : name[..180];
    }
}
