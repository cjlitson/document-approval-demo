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
        var model = new DashboardViewModel
        {
            MyOpenRequests = await db.Requests.CountAsync(x => x.RequesterId == userId && (x.Status == RequestStatus.InApproval || x.Status == RequestStatus.Rejected), cancellationToken),
            MyPendingApprovals = await db.ApprovalInstances.CountAsync(x => x.ApproverId == userId && x.Status == ApprovalStatus.Pending, cancellationToken),
            ApprovedRequests = await db.Requests.CountAsync(x => x.RequesterId == userId && x.Status == RequestStatus.Approved, cancellationToken),
            RecentRequests = await db.Requests.AsNoTracking().Where(x => x.RequesterId == userId)
                .OrderByDescending(x => x.CreatedAtUtc).Take(5).ToListAsync(cancellationToken)
        };
        return View(model);
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}

