using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface IDocumentAuthorizationService
{
    Task<bool> CanViewRequestAsync(Guid requestId, Guid? userId, bool isSystemAdmin, CancellationToken cancellationToken = default);
    Task<bool> CanOverseeDocumentTypeAsync(Guid documentTypeId, Guid? userId, bool isSystemAdmin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetOverseenDocumentTypeIdsAsync(Guid userId, bool isSystemAdmin, CancellationToken cancellationToken = default);
}

public sealed class DocumentAuthorizationService(AppDbContext db) : IDocumentAuthorizationService
{
    public Task<bool> CanViewRequestAsync(
        Guid requestId,
        Guid? userId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue) return Task.FromResult(false);
        if (isSystemAdmin) return db.Requests.AsNoTracking().AnyAsync(x => x.Id == requestId, cancellationToken);

        var id = userId.Value;
        return db.Requests.AsNoTracking().AnyAsync(x =>
            x.Id == requestId &&
            (x.RequesterId == id ||
             x.Approvals.Any(approval => approval.ApproverId == id) ||
             x.DocumentType.AccessAssignments.Any(access => access.UserId == id && access.IsActive) ||
             db.NotificationOutbox.Any(notification => notification.RequestId == x.Id && notification.UserId == id)),
            cancellationToken);
    }

    public Task<bool> CanOverseeDocumentTypeAsync(
        Guid documentTypeId,
        Guid? userId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue) return Task.FromResult(false);
        if (isSystemAdmin) return db.DocumentTypes.AsNoTracking().AnyAsync(x => x.Id == documentTypeId, cancellationToken);

        return db.DocumentTypeAccess.AsNoTracking().AnyAsync(
            x => x.DocumentTypeId == documentTypeId && x.UserId == userId.Value && x.IsActive,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetOverseenDocumentTypeIdsAsync(
        Guid userId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        if (isSystemAdmin)
            return await db.DocumentTypes.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);

        return await db.DocumentTypeAccess.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.DocumentTypeId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
