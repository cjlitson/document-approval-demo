using System.Security.Claims;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Task<ApplicationUser?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, AppDbContext db) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var raw = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Task<ApplicationUser?> GetAsync(CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : Task.FromResult<ApplicationUser?>(null);
}

