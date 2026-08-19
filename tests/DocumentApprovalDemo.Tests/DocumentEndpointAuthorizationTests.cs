using System.Security.Claims;
using System.Text;
using DocumentApprovalDemo.Controllers;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class DocumentEndpointAuthorizationTests
{
    [Fact]
    public async Task PreviewAndDownload_RequireRequestAuthorizationAndExactAttachmentPair()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddRequestAsync("PUR-DOC-0001", RequestStatus.InApproval, "first.pdf");
        var second = await fixture.AddRequestAsync("PUR-DOC-0002", RequestStatus.InApproval, "second.pdf");
        var taylor = await fixture.Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.PurchasingId);
        var controller = fixture.Controller(taylor);

        var preview = Assert.IsType<FileStreamResult>(await controller.PreviewAttachment(first.Request.Id, first.Attachment.Id, CancellationToken.None));
        Assert.Equal("application/pdf", preview.ContentType);
        Assert.True(preview.EnableRangeProcessing);
        await preview.FileStream.DisposeAsync();

        var download = Assert.IsType<FileStreamResult>(await controller.DownloadAttachment(first.Request.Id, first.Attachment.Id, CancellationToken.None));
        Assert.Equal(first.Attachment.OriginalFileName, download.FileDownloadName);
        await download.FileStream.DisposeAsync();

        Assert.IsType<NotFoundResult>(await controller.PreviewAttachment(first.Request.Id, second.Attachment.Id, CancellationToken.None));

        var unrelated = await fixture.Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.PresidentId);
        var unauthorized = fixture.Controller(unrelated);
        Assert.IsType<ForbidResult>(await unauthorized.PreviewAttachment(first.Request.Id, first.Attachment.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await unauthorized.DownloadAttachment(first.Request.Id, first.Attachment.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Package_RequiresAuthorizationAndFinalApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var completed = await fixture.AddRequestAsync("PUR-DOC-0003", RequestStatus.Approved, "completed.pdf");
        var taylor = await fixture.Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.PurchasingId);
        var controller = fixture.Controller(taylor);
        var package = Assert.IsType<FileContentResult>(await controller.Package(completed.Request.Id, CancellationToken.None));
        Assert.Equal("application/zip", package.ContentType);
        var approvalPreview = Assert.IsType<FileContentResult>(
            await controller.ApprovalRecordPreview(completed.Request.Id, CancellationToken.None));
        Assert.Equal("application/pdf", approvalPreview.ContentType);
        var approvalDownload = Assert.IsType<FileContentResult>(
            await controller.ApprovalRecordDownload(completed.Request.Id, CancellationToken.None));
        Assert.Equal("PUR-DOC-0003-Approval-Record.pdf", approvalDownload.FileDownloadName);

        completed.Request.Status = RequestStatus.InApproval;
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await controller.Package(completed.Request.Id, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(
            await controller.ApprovalRecordPreview(completed.Request.Id, CancellationToken.None));

        var unrelated = await fixture.Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.PresidentId);
        completed.Request.Status = RequestStatus.Approved;
        await fixture.Db.SaveChangesAsync();
        var unauthorized = fixture.Controller(unrelated);
        Assert.IsType<ForbidResult>(await unauthorized.Package(completed.Request.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(
            await unauthorized.ApprovalRecordDownload(completed.Request.Id, CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly MemoryFileStorage storage = new();
        private readonly FakePackageService package = new();

        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            Connection = connection;
            Db = db;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await DemoDataSeeder.SeedAsync(db);
            return new Fixture(connection, db);
        }

        public async Task<(ApprovalRequest Request, RequestAttachment Attachment)> AddRequestAsync(
            string number,
            RequestStatus status,
            string storedName)
        {
            var requester = await Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
            var type = await Db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PurchaseDocumentTypeId);
            var attachment = new RequestAttachment
            {
                OriginalFileName = storedName,
                StoredFileName = storedName,
                ContentType = "application/pdf",
                RevisionNumber = 1
            };
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nsecure endpoint test");
            attachment.SizeBytes = bytes.Length;
            storage.Files[storedName] = bytes;
            var request = new ApprovalRequest
            {
                RequestNumber = number,
                DocumentType = type,
                DocumentTypeId = type.Id,
                Requester = requester,
                RequesterId = requester.Id,
                ConfirmedManagerId = DemoDataSeeder.ManagerId,
                Title = "Document endpoint test",
                Department = requester.Department,
                Status = status,
                CurrentRevisionNumber = 1,
                CompletedAtUtc = status == RequestStatus.Approved ? DateTimeOffset.UtcNow : null
            };
            attachment.Request = request;
            request.Attachments.Add(attachment);
            Db.Requests.Add(request);
            await Db.SaveChangesAsync();
            return (request, attachment);
        }

        public RequestsController Controller(ApplicationUser user)
        {
            var controller = new RequestsController(
                Db,
                new TestCurrentUser(user),
                new NoOpWorkflow(),
                storage,
                new FilePreviewService(),
                new DocumentAuthorizationService(Db),
                new FakeApprovalRecord(),
                package);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        user.Roles.Select(role => new Claim(ClaimTypes.Role, role))
                            .Append(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())),
                        "test"))
                }
            };
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class TestCurrentUser(ApplicationUser user) : ICurrentUserService
    {
        public Guid? UserId => user.Id;
        public Task<ApplicationUser?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationUser?>(user);
    }

    private sealed class NoOpWorkflow : IWorkflowService
    {
        public Task StartAsync(ApprovalRequest request, ApplicationUser actor, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DecisionResult> DecideAsync(Guid approvalId, Guid actorId, DecisionType decision, string typedSignature, string? comments, CancellationToken cancellationToken = default) => Task.FromResult(new DecisionResult(true));
        public Task RestartAsync(ApprovalRequest request, ApplicationUser actor, string changeSummary, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryFileStorage : IFileStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Files[storedFileName], writable: false));
    }

    private sealed class FakeApprovalRecord : IApprovalRecordService
    {
        public ApprovalRecordModel CreateModel(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null) => new();
        public byte[] Build(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null) => Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
    }

    private sealed class FakePackageService : IDocumentPackageService
    {
        public Task<byte[]> BuildAsync(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Encoding.ASCII.GetBytes("PK package"));
    }
}
