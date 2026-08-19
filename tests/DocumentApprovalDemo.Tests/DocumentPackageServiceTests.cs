using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class DocumentPackageServiceTests
{
    [Fact]
    public async Task Package_ContainsApprovalRecordRevisionedAttachmentsManifestAndMatchingHashes()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\nexample");
        var textBytes = Encoding.UTF8.GetBytes("current revision notes");
        var storage = new MemoryFileStorage(new Dictionary<string, byte[]>
        {
            ["first.pdf"] = pdfBytes,
            ["second.txt"] = textBytes
        });
        var request = ApprovedRequest();
        request.Attachments.Add(new RequestAttachment
        {
            StoredFileName = "first.pdf",
            OriginalFileName = "Vendor Quote.pdf",
            ContentType = "application/pdf",
            RevisionNumber = 1,
            SizeBytes = pdfBytes.Length
        });
        request.Attachments.Add(new RequestAttachment
        {
            StoredFileName = "second.txt",
            OriginalFileName = "Notes.txt",
            ContentType = "text/plain",
            RevisionNumber = 2,
            SizeBytes = textBytes.Length
        });
        var service = new DocumentPackageService(new FakeApprovalRecord(), storage, new FilePreviewService());

        var bytes = await service.BuildAsync(request);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("PUR-2026-0048-Approval-Record.pdf"));
        Assert.NotNull(archive.GetEntry("Attachments/Revision-01/Vendor Quote.pdf"));
        Assert.NotNull(archive.GetEntry("Attachments/Revision-02/Notes.txt"));
        var manifestEntry = Assert.Single(archive.Entries.Where(x => x.FullName == "Package-Manifest.json"));
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<DocumentPackageManifest>(
            manifestStream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(manifest);
        Assert.Equal(request.Id, manifest.RequestId);
        Assert.Equal(2, manifest.Revision);
        Assert.Equal(2, manifest.Attachments.Count);

        foreach (var item in manifest.Attachments)
        {
            var entry = Assert.Single(archive.Entries.Where(x => x.FullName == item.PackagedFilename));
            await using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer);
            var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
            Assert.Equal(item.Sha256, hash);
            Assert.Equal(buffer.Length, item.Size);
        }
    }

    [Fact]
    public async Task Package_IsBlockedBeforeFinalApproval()
    {
        var request = ApprovedRequest();
        request.Status = RequestStatus.InApproval;
        var service = new DocumentPackageService(
            new FakeApprovalRecord(),
            new MemoryFileStorage(new Dictionary<string, byte[]>()),
            new FilePreviewService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync(request));
    }

    private static ApprovalRequest ApprovedRequest() => new()
    {
        Id = Guid.Parse("40000000-0000-0000-0000-000000000048"),
        RequestNumber = "PUR-2026-0048",
        Title = "Package test",
        DocumentType = new DocumentType { Name = "Purchase Request" },
        Requester = new ApplicationUser { FullName = "Avery Employee" },
        Status = RequestStatus.Approved,
        CurrentRevisionNumber = 2,
        RouteVersion = new ApprovalRouteVersion { VersionNumber = 3 }
    };

    private sealed class FakeApprovalRecord : IApprovalRecordService
    {
        public ApprovalRecordModel CreateModel(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null) => new();
        public byte[] Build(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null) =>
            Encoding.ASCII.GetBytes("%PDF-1.4\napproval record\n%%EOF");
    }

    private sealed class MemoryFileStorage(IReadOnlyDictionary<string, byte[]> files) : IFileStorageService
    {
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(files[storedFileName], writable: false));
    }
}
