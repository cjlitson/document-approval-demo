using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize]
[Route("requests")]
public sealed class RequestsController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IWorkflowService workflow,
    IFileStorageService fileStorage,
    ISignedPackageService signedPackage) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        return View(await db.Requests.AsNoTracking().Where(x => x.RequesterId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        var model = new RequestFormViewModel
        {
            Department = user.Department,
            ManagerId = user.ManagerId,
            ManagerSource = user.ManagerId.HasValue ? "Entra" : "RequesterSelected"
        };
        await PopulateManagersAsync(model, user.Id, cancellationToken);
        return View(model);
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RequestFormViewModel model, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        if (model.ManagerId == user.Id) ModelState.AddModelError(nameof(model.ManagerId), "You cannot select yourself as manager.");
        if (model.SupportingDocuments.Count == 0 || model.SupportingDocuments.All(x => x.Length == 0))
            ModelState.AddModelError(nameof(model.SupportingDocuments), "At least one supporting document is required.");
        var manager = model.ManagerId.HasValue
            ? await db.Users.SingleOrDefaultAsync(x => x.Id == model.ManagerId && x.IsActive, cancellationToken)
            : null;
        if (manager is null) ModelState.AddModelError(nameof(model.ManagerId), "Select an active manager.");
        if (!ModelState.IsValid)
        {
            await PopulateManagersAsync(model, user.Id, cancellationToken);
            return View(model);
        }

        var request = new ApprovalRequest
        {
            RequestNumber = await NextRequestNumberAsync(cancellationToken),
            RequesterId = user.Id,
            ConfirmedManagerId = manager!.Id,
            ManagerSource = user.ManagerId == manager.Id ? "EntraConfirmed" : "RequesterSelected",
            Title = model.Title.Trim(),
            Subcategory = model.Subcategory,
            Vendor = model.Vendor.Trim(),
            PurchaseLink = model.PurchaseLink?.Trim(),
            Department = model.Department.Trim(),
            Amount = model.Amount,
            BusinessJustification = model.BusinessJustification.Trim(),
            CurrentRevisionNumber = 1
        };
        request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1, ChangeSummary = "Initial submission" });

        foreach (var file in model.SupportingDocuments.Where(x => x.Length > 0))
        {
            try
            {
                var stored = await fileStorage.SaveAsync(file, cancellationToken);
                request.Attachments.Add(new RequestAttachment
                {
                    Request = request, RevisionNumber = 1, OriginalFileName = stored.OriginalFileName,
                    StoredFileName = stored.StoredFileName, ContentType = stored.ContentType, SizeBytes = stored.SizeBytes
                });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.SupportingDocuments), ex.Message);
                await PopulateManagersAsync(model, user.Id, cancellationToken);
                return View(model);
            }
        }

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await workflow.StartAsync(request, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var request = await LoadDetailsAsync(id, cancellationToken);
        if (request is null) return NotFound();
        if (!CanView(request)) return Forbid();
        return View(request);
    }

    [HttpGet("{id:guid}/revise")]
    public async Task<IActionResult> Revise(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return NotFound();
        if (request.RequesterId != currentUser.UserId || request.Status != RequestStatus.Rejected) return Forbid();
        return View(new ReviseRequestViewModel
        {
            RequestId = request.Id, Title = request.Title, Subcategory = request.Subcategory, Vendor = request.Vendor,
            PurchaseLink = request.PurchaseLink, Amount = request.Amount, BusinessJustification = request.BusinessJustification
        });
    }

    [HttpPost("{id:guid}/revise")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise(Guid id, ReviseRequestViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.RequestId) return BadRequest();
        var request = await db.Requests.Include(x => x.Revisions).Include(x => x.Approvals).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return NotFound();
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        if (request.RequesterId != user.Id || request.Status != RequestStatus.Rejected) return Forbid();
        if (model.SupportingDocuments.Count == 0 || model.SupportingDocuments.All(x => x.Length == 0))
            ModelState.AddModelError(nameof(model.SupportingDocuments), "Upload at least one supporting document for the new revision.");
        if (!ModelState.IsValid) return View(model);

        request.Title = model.Title.Trim();
        request.Subcategory = model.Subcategory;
        request.Vendor = model.Vendor.Trim();
        request.PurchaseLink = model.PurchaseLink?.Trim();
        request.Amount = model.Amount;
        request.BusinessJustification = model.BusinessJustification.Trim();
        var nextRevision = request.CurrentRevisionNumber + 1;
        foreach (var file in model.SupportingDocuments.Where(x => x.Length > 0))
        {
            try
            {
                var stored = await fileStorage.SaveAsync(file, cancellationToken);
                var attachment = new RequestAttachment
                {
                    Request = request, RevisionNumber = nextRevision, OriginalFileName = stored.OriginalFileName,
                    StoredFileName = stored.StoredFileName, ContentType = stored.ContentType, SizeBytes = stored.SizeBytes
                };
                request.Attachments.Add(attachment);
                db.Attachments.Add(attachment);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.SupportingDocuments), ex.Message);
                return View(model);
            }
        }
        await workflow.RestartAsync(request, user, model.ChangeSummary, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{requestId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Attachment(Guid requestId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var request = await db.Requests.Include(x => x.Approvals).SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null) return NotFound();
        if (!CanView(request)) return Forbid();
        var attachment = await db.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId && x.RequestId == requestId, cancellationToken);
        if (attachment is null) return NotFound();
        var stream = await fileStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
        return File(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    [HttpGet("{id:guid}/signed-package")]
    public async Task<IActionResult> SignedPackage(Guid id, CancellationToken cancellationToken)
    {
        var request = await LoadDetailsAsync(id, cancellationToken);
        if (request is null) return NotFound();
        if (!CanView(request)) return Forbid();
        if (request.Status != RequestStatus.Approved) return BadRequest("The signed package is available after final approval.");
        var bytes = signedPackage.Build(request);
        return File(bytes, "application/pdf", $"{request.RequestNumber}-signed-package.pdf");
    }

    private bool CanView(ApprovalRequest request) =>
        request.RequesterId == currentUser.UserId ||
        request.Approvals.Any(x => x.ApproverId == currentUser.UserId) ||
        User.IsInRole(Roles.SystemAdmin);

    private Task<ApprovalRequest?> LoadDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Requests
            .Include(x => x.Requester).Include(x => x.ConfirmedManager).Include(x => x.RouteVersion)
            .Include(x => x.Revisions)
            .Include(x => x.Attachments)
            .Include(x => x.Approvals).ThenInclude(x => x.Approver)
            .Include(x => x.Approvals).ThenInclude(x => x.Decision)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task PopulateManagersAsync(RequestFormViewModel model, Guid userId, CancellationToken cancellationToken)
    {
        model.Managers = await db.Users.AsNoTracking().Where(x => x.IsActive && x.Id != userId)
            .OrderBy(x => x.FullName).Select(x => new SelectListItem(x.FullName + " — " + x.Email, x.Id.ToString())).ToListAsync(cancellationToken);
    }

    private async Task<string> NextRequestNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var count = await db.Requests.CountAsync(x => x.CreatedAtUtc.Year == year, cancellationToken) + 1;
        return $"PR-{year}-{count:0000}";
    }
}
