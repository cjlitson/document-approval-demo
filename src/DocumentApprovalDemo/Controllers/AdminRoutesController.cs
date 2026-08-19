using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize(Roles = Roles.SystemAdmin)]
[Route("admin/routes")]
public sealed class AdminRoutesController(AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var versions = await db.RouteVersions.AsNoTracking()
            .Include(x => x.Route).ThenInclude(x => x.DocumentType)
            .Include(x => x.Stages).ThenInclude(x => x.ConditionGroups).ThenInclude(x => x.Rules).ThenInclude(x => x.Operands)
            .Include(x => x.Stages).ThenInclude(x => x.AlertPolicies)
            .ToListAsync(cancellationToken);
        return View(versions
            .OrderBy(x => x.Route.DocumentType.Name)
            .ThenByDescending(x => x.VersionNumber)
            .ToList());
    }

    [HttpGet("designer")]
    public IActionResult Designer() => Redirect("/route-designer");
}
