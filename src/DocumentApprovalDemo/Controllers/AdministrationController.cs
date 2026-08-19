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

}
