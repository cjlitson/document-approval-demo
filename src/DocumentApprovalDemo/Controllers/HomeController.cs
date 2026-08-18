using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize]
public sealed class HomeController(AppDbContext db, ICurrentUserService currentUser) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var myRequests = await db.Requests.AsNoTracking()
            .Include(x => x.DocumentType)
            .Include(x => x.Approvals)
            .Where(x => x.RequesterId == userId)
            .ToListAsync(cancellationToken);

        var pendingApprovals = await db.ApprovalInstances.AsNoTracking()
            .Include(x => x.Request).ThenInclude(x => x.Requester)
            .Include(x => x.Request).ThenInclude(x => x.DocumentType)
            .Where(x => x.ApproverId == userId && x.Status == ApprovalStatus.Pending)
            .ToListAsync(cancellationToken);

        var accessibleRequests = await db.Requests.AsNoTracking()
            .Where(x => x.RequesterId == userId || x.Approvals.Any(approval => approval.ApproverId == userId))
            .Select(x => new { x.Id, x.RequestNumber })
            .ToListAsync(cancellationToken);
        var requestLookup = accessibleRequests.ToDictionary(x => x.Id);
        var requestIds = requestLookup.Keys.ToList();

        List<AuditEvent> auditEvents = requestIds.Count == 0
            ? []
            : await db.AuditEvents.AsNoTracking()
                .Where(x => x.RequestId.HasValue && requestIds.Contains(x.RequestId.Value))
                .ToListAsync(cancellationToken);
        var actorIds = auditEvents.Select(x => x.ActorUserId).Distinct().ToList();
        var actorLookup = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(x => actorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var completedCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var model = new DashboardViewModel
        {
            MyPendingApprovals = pendingApprovals.Count,
            ActiveRequests = myRequests.Count(x => x.Status == RequestStatus.InApproval),
            NeedsAttention = myRequests.Count(x => x.Status == RequestStatus.Rejected),
            CompletedLast30Days = myRequests.Count(x =>
                x.Status == RequestStatus.Approved &&
                x.CompletedAtUtc is { } completedAt &&
                completedAt >= completedCutoff),
            UnreadAlerts = await db.NotificationOutbox.CountAsync(x => x.UserId == userId && !x.IsRead && x.Status != NotificationStatus.Cancelled, cancellationToken),
            ApprovalQueue = pendingApprovals
                .OrderBy(x => x.ActivatedAtUtc)
                .Take(7)
                .Select(x => new DashboardApprovalQueueItemViewModel
                {
                    ApprovalId = x.Id,
                    RequestId = x.RequestId,
                    RequestNumber = x.Request.RequestNumber,
                    Title = x.Request.Title,
                    DocumentTypeName = x.Request.DocumentType.Name,
                    RequesterName = x.Request.Requester.FullName,
                    StageName = x.StageName,
                    SubmittedAtUtc = x.Request.SubmittedAtUtc,
                    ActivatedAtUtc = x.ActivatedAtUtc
                })
                .ToList(),
            RecentRequests = myRequests
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(5)
                .Select(x => new DashboardRequestViewModel
                {
                    Id = x.Id,
                    RequestNumber = x.RequestNumber,
                    Title = x.Title,
                    DocumentTypeName = x.DocumentType.Name,
                    CurrentStage = GetCurrentStage(x),
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc
                })
                .ToList(),
            RecentActivity = auditEvents
                .OrderByDescending(x => x.OccurredAtUtc)
                .Take(8)
                .Select(x => new DashboardActivityItemViewModel
                {
                    RequestId = x.RequestId!.Value,
                    RequestNumber = requestLookup[x.RequestId.Value].RequestNumber,
                    EventType = x.EventType,
                    Details = x.Details,
                    ActorName = actorLookup.GetValueOrDefault(x.ActorUserId, "System"),
                    OccurredAtUtc = x.OccurredAtUtc
                })
                .ToList()
        };
        return View(model);
    }

    [HttpGet("/help")]
    public IActionResult Help() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();

    private static string GetCurrentStage(ApprovalRequest request)
    {
        if (request.Status == RequestStatus.Approved) return "Workflow completed";
        if (request.Status == RequestStatus.Rejected) return "Revision required";
        if (request.Status == RequestStatus.Draft) return "Draft";

        return request.Approvals
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber && x.Status == ApprovalStatus.Pending)
            .Select(x => x.StageName)
            .SingleOrDefault() ?? "In approval";
    }
}
