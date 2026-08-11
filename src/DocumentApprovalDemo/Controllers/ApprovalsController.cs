using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize]
[Route("approvals")]
public sealed class ApprovalsController(AppDbContext db, ICurrentUserService currentUser, IWorkflowService workflow) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Inbox(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        return View(await db.ApprovalInstances.AsNoTracking().Include(x => x.Request).ThenInclude(x => x.Requester)
            .Where(x => x.ApproverId == userId && x.Status == ApprovalStatus.Pending)
            .OrderBy(x => x.ActivatedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        var approval = await db.ApprovalInstances.AsNoTracking()
            .Include(x => x.Request).ThenInclude(x => x.Requester)
            .Include(x => x.Request).ThenInclude(x => x.Attachments)
            .Include(x => x.Approver)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (approval is null) return NotFound();
        if (approval.ApproverId != currentUser.UserId) return Forbid();
        ViewBag.Decision = new ApprovalDecisionViewModel { ApprovalId = id };
        return View(approval);
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(Guid id, ApprovalDecisionViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ApprovalId) return BadRequest();
        if (!ModelState.IsValid) return await Review(id, cancellationToken);
        var result = await workflow.DecideAsync(id, currentUser.UserId!.Value, model.Decision, model.TypedSignature, model.Comments, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return await Review(id, cancellationToken);
        }
        return RedirectToAction(nameof(Inbox));
    }
}

