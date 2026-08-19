using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class DocumentAuthorizationServiceTests
{
    [Fact]
    public async Task ScopedAccess_GrantsOnlyAssignedDocumentType_WhileSystemAdminSeesAll()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);

        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        var purchase = await db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PurchaseDocumentTypeId);
        var policy = await db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PolicyDocumentTypeId);
        var purchaseRequest = Request("PUR-AUTH-0001", purchase, requester);
        var policyRequest = Request("POL-AUTH-0001", policy, requester);
        db.Requests.AddRange(purchaseRequest, policyRequest);
        await db.SaveChangesAsync();

        var service = new DocumentAuthorizationService(db);
        var taylor = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.PurchasingId);
        Assert.False(taylor.IsInRole(Roles.SystemAdmin));
        Assert.True(await service.CanViewRequestAsync(purchaseRequest.Id, taylor.Id, false));
        Assert.False(await service.CanViewRequestAsync(policyRequest.Id, taylor.Id, false));
        Assert.True(await service.CanOverseeDocumentTypeAsync(purchase.Id, taylor.Id, false));
        Assert.False(await service.CanOverseeDocumentTypeAsync(policy.Id, taylor.Id, false));

        var coordinator = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.CoordinatorId);
        Assert.True(await service.CanViewRequestAsync(purchaseRequest.Id, coordinator.Id, false));
        Assert.False(await service.CanViewRequestAsync(policyRequest.Id, coordinator.Id, false));

        Assert.True(await service.CanViewRequestAsync(purchaseRequest.Id, DemoDataSeeder.AdminId, true));
        Assert.True(await service.CanViewRequestAsync(policyRequest.Id, DemoDataSeeder.AdminId, true));
    }

    private static ApprovalRequest Request(string number, DocumentType type, ApplicationUser requester) => new()
    {
        RequestNumber = number,
        DocumentTypeId = type.Id,
        DocumentType = type,
        RequesterId = requester.Id,
        Requester = requester,
        ConfirmedManagerId = DemoDataSeeder.ManagerId,
        Title = "Authorization test",
        Department = requester.Department,
        Status = RequestStatus.InApproval
    };
}
