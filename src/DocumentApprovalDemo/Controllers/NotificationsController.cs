using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize]
[Route("notifications")]
public sealed class NotificationsController(
    AppDbContext db,
    ICurrentUserService currentUser,
    INotificationDispatcher dispatcher) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var notifications = await db.NotificationOutbox.AsNoTracking()
            .Include(x => x.Request)
            .Include(x => x.DeliveryAttempts)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var ordered = notifications.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return View(new NotificationCenterViewModel
        {
            Notifications = ordered,
            PendingCount = ordered.Count(x => x.Status == NotificationStatus.Pending),
            DeliveredCount = ordered.Count(x => x.Status == NotificationStatus.Delivered),
            CancelledCount = ordered.Count(x => x.Status == NotificationStatus.Cancelled)
        });
    }

    [HttpPost("{id:guid}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await db.NotificationOutbox.SingleOrDefaultAsync(
            x => x.Id == id && x.UserId == currentUser.UserId, cancellationToken);
        if (notification is null) return NotFound();
        notification.IsRead = true;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.SystemAdmin)]
    [HttpPost("simulate-due")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateDue(CancellationToken cancellationToken)
    {
        var pending = await db.NotificationOutbox.Where(x => x.Status == NotificationStatus.Pending).ToListAsync(cancellationToken);
        foreach (var notification in pending) notification.DueAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var delivered = await dispatcher.DispatchDueAsync(cancellationToken);
        TempData["Success"] = $"Simulated {delivered} due deliveries. No external email or Teams message was sent.";
        return RedirectToAction(nameof(Index));
    }
}
