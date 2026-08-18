using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize(Roles = Roles.SystemAdmin)]
[Route("admin")]
public sealed class AdministrationController(AppDbContext db) : Controller
{
    [HttpGet("requests")]
    public async Task<IActionResult> Requests(CancellationToken cancellationToken)
    {
        var requests = await db.Requests.AsNoTracking()
            .Include(x => x.DocumentType)
            .Include(x => x.Requester)
            .ToListAsync(cancellationToken);
        return View(requests.OrderByDescending(x => x.CreatedAtUtc).ToList());
    }

    [HttpGet("document-types")]
    public async Task<IActionResult> DocumentTypes(CancellationToken cancellationToken)
    {
        var documentTypes = await db.DocumentTypes.AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.Routes).ThenInclude(x => x.Versions)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return View(documentTypes);
    }
}
