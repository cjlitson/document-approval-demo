using System.Globalization;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize(Roles = Roles.SystemAdmin)]
[Route("admin/routes")]
public sealed class AdminRoutesController(AppDbContext db, ICurrentUserService currentUser) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await db.RouteVersions.AsNoTracking().Include(x => x.Route).Include(x => x.Stages).ThenInclude(x => x.Rules)
            .OrderByDescending(x => x.VersionNumber).ToListAsync(cancellationToken));

    [HttpPost("new-version")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewVersion(CancellationToken cancellationToken)
    {
        if (await db.RouteVersions.AnyAsync(x => x.Status == RouteVersionStatus.Draft, cancellationToken))
        {
            TempData["Error"] = "A draft already exists.";
            return RedirectToAction(nameof(Index));
        }

        var source = await db.RouteVersions.Include(x => x.Stages).ThenInclude(x => x.Rules)
            .OrderByDescending(x => x.VersionNumber).FirstAsync(x => x.Status == RouteVersionStatus.Published, cancellationToken);
        var draft = new ApprovalRouteVersion
        {
            RouteId = source.RouteId, VersionNumber = source.VersionNumber + 1,
            Name = $"Pilot route v{source.VersionNumber + 1}", Status = RouteVersionStatus.Draft
        };
        foreach (var stage in source.Stages.OrderBy(x => x.Sequence))
        {
            var copy = new ApprovalRouteStage
            {
                RouteVersion = draft, Sequence = stage.Sequence, Name = stage.Name,
                AssignmentType = stage.AssignmentType, NamedApproverId = stage.NamedApproverId, IsConditional = stage.IsConditional
            };
            foreach (var rule in stage.Rules)
                copy.Rules.Add(new RouteRule { Stage = copy, Field = rule.Field, Operator = rule.Operator, Value = rule.Value });
            draft.Stages.Add(copy);
        }
        db.RouteVersions.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Edit), new { id = draft.Id });
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var draft = await db.RouteVersions.AsNoTracking().Include(x => x.Stages).ThenInclude(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == RouteVersionStatus.Draft, cancellationToken);
        if (draft is null) return NotFound();
        var president = draft.Stages.Single(x => x.Name == "President");
        var finance = draft.Stages.Single(x => x.Name == "VP Finance");
        var rule = president.Rules.Single(x => x.Field == RuleField.Amount);
        var model = new RouteDraftViewModel
        {
            VersionId = id, Name = draft.Name, PresidentApproverId = president.NamedApproverId,
            FinanceApproverId = finance.NamedApproverId, PresidentAmountOperator = rule.Operator,
            PresidentAmountThreshold = decimal.Parse(rule.Value, CultureInfo.InvariantCulture)
        };
        await PopulateApproversAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, RouteDraftViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.VersionId) return BadRequest();
        if (!ModelState.IsValid)
        {
            await PopulateApproversAsync(model, cancellationToken);
            return View(model);
        }
        var draft = await db.RouteVersions.Include(x => x.Stages).ThenInclude(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == RouteVersionStatus.Draft, cancellationToken);
        if (draft is null) return NotFound();
        draft.Name = model.Name.Trim();
        var president = draft.Stages.Single(x => x.Name == "President");
        president.NamedApproverId = model.PresidentApproverId;
        var rule = president.Rules.Single(x => x.Field == RuleField.Amount);
        rule.Operator = model.PresidentAmountOperator;
        rule.Value = model.PresidentAmountThreshold.ToString(CultureInfo.InvariantCulture);
        draft.Stages.Single(x => x.Name == "VP Finance").NamedApproverId = model.FinanceApproverId;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Draft saved. Published versions remain unchanged.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var draft = await db.RouteVersions.SingleOrDefaultAsync(x => x.Id == id && x.Status == RouteVersionStatus.Draft, cancellationToken);
        if (draft is null) return NotFound();
        foreach (var published in await db.RouteVersions.Where(x => x.RouteId == draft.RouteId && x.Status == RouteVersionStatus.Published).ToListAsync(cancellationToken))
            published.Status = RouteVersionStatus.Retired;
        draft.Status = RouteVersionStatus.Published;
        draft.PublishedAtUtc = DateTimeOffset.UtcNow;
        draft.PublishedById = currentUser.UserId;
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = currentUser.UserId!.Value,
            EventType = "RoutePublished",
            Details = $"Published route version {draft.VersionNumber}."
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateApproversAsync(RouteDraftViewModel model, CancellationToken cancellationToken)
    {
        model.Approvers = await db.Users.AsNoTracking().Where(x => x.IsActive && x.RolesCsv.Contains(Roles.Approver))
            .OrderBy(x => x.FullName).Select(x => new SelectListItem(x.FullName + " — " + x.Email, x.Id.ToString())).ToListAsync(cancellationToken);
    }
}
