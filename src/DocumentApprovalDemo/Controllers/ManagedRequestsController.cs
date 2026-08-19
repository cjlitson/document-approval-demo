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
[Route("managed-requests")]
public sealed class ManagedRequestsController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IDocumentAuthorizationService authorization) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid? documentTypeId,
        string? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var isSystemAdmin = User.IsInRole(Roles.SystemAdmin);
        var allowedTypeIds = await authorization.GetOverseenDocumentTypeIdsAsync(userId, isSystemAdmin, cancellationToken);
        if (allowedTypeIds.Count == 0) return Forbid();
        if (documentTypeId.HasValue && !allowedTypeIds.Contains(documentTypeId.Value)) return Forbid();

        var query = db.Requests.AsNoTracking()
            .Include(x => x.DocumentType)
            .Include(x => x.Requester)
            .Include(x => x.Approvals)
            .Where(x => allowedTypeIds.Contains(x.DocumentTypeId));
        if (documentTypeId.HasValue) query = query.Where(x => x.DocumentTypeId == documentTypeId.Value);
        if (Enum.TryParse<RequestStatus>(status, true, out var parsedStatus)) query = query.Where(x => x.Status == parsedStatus);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.RequestNumber.Contains(term) || x.Title.Contains(term) || x.Requester.FullName.Contains(term));
        }

        var requests = await query.ToListAsync(cancellationToken);
        var documentTypes = await db.DocumentTypes.AsNoTracking()
            .Where(x => allowedTypeIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == documentTypeId))
            .ToListAsync(cancellationToken);
        return View(new ManagedRequestsViewModel
        {
            DocumentTypeId = documentTypeId,
            Status = status,
            Search = search,
            DocumentTypes = documentTypes,
            Requests = requests.OrderByDescending(x => x.CreatedAtUtc).Select(Map).ToList()
        });
    }

    private static ManagedRequestRowViewModel Map(ApprovalRequest request) => new()
    {
        Id = request.Id,
        RequestNumber = request.RequestNumber,
        Title = request.Title,
        DocumentTypeName = request.DocumentType.Name,
        RequesterName = request.Requester.FullName,
        Status = request.Status,
        CurrentStep = request.Status switch
        {
            RequestStatus.Approved => "Completed",
            RequestStatus.Rejected => "Revision required",
            RequestStatus.Draft => "Draft",
            _ => request.Approvals.Where(x => x.RevisionNumber == request.CurrentRevisionNumber && x.Status == ApprovalStatus.Pending)
                     .Select(x => x.StageName).SingleOrDefault() ?? "In approval"
        },
        CreatedAtUtc = request.CreatedAtUtc,
        CompletedAtUtc = request.CompletedAtUtc,
        NeedsAttention = request.Status == RequestStatus.Rejected
    };
}
