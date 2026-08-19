using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class DocumentTypeAdministrationServiceTests
{
    [Fact]
    public async Task CreateDeactivateReactivateAndDeleteUnused_ArePersistedAndAudited()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var service = new DocumentTypeAdministrationService(db);

        var created = await service.CreateAsync(Definition("travel-authorization", "TRA"), DemoDataSeeder.AdminId);
        Assert.True(created.Succeeded, created.Error);
        var id = created.DocumentTypeId!.Value;
        var type = await db.DocumentTypes.Include(x => x.Routes).ThenInclude(x => x.Versions).SingleAsync(x => x.Id == id);
        Assert.True(type.IsActive);
        Assert.Single(type.Fields);
        Assert.Single(type.AccessAssignments);
        Assert.Single(type.LifecycleNotificationRules);
        Assert.Single(type.Routes.Single().Versions);
        Assert.Equal(RouteVersionStatus.Draft, type.Routes.Single().Versions.Single().Status);

        Assert.True((await service.SetActiveAsync(id, false, DemoDataSeeder.AdminId)).Succeeded);
        Assert.False((await db.DocumentTypes.SingleAsync(x => x.Id == id)).IsActive);
        Assert.True((await service.SetActiveAsync(id, true, DemoDataSeeder.AdminId)).Succeeded);
        Assert.True((await db.DocumentTypes.SingleAsync(x => x.Id == id)).IsActive);

        var wrongConfirmation = await service.DeleteUnusedAsync(id, "wrong", DemoDataSeeder.AdminId);
        Assert.False(wrongConfirmation.Succeeded);
        var deleted = await service.DeleteUnusedAsync(id, "DELETE", DemoDataSeeder.AdminId);
        Assert.True(deleted.Succeeded, deleted.Error);
        Assert.False(await db.DocumentTypes.AnyAsync(x => x.Id == id));
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "UnusedDocumentTypeDeleted");
    }

    [Fact]
    public async Task DeleteUnused_IsBlockedWhenRequestHistoryExists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var service = new DocumentTypeAdministrationService(db);
        var created = await service.CreateAsync(Definition("expense-claim", "EXP"), DemoDataSeeder.AdminId);
        var id = created.DocumentTypeId!.Value;
        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        var type = await db.DocumentTypes.SingleAsync(x => x.Id == id);
        db.Requests.Add(new ApprovalRequest
        {
            RequestNumber = "EXP-2026-0001",
            DocumentType = type,
            DocumentTypeId = id,
            Requester = requester,
            RequesterId = requester.Id,
            ConfirmedManagerId = DemoDataSeeder.ManagerId,
            Title = "Historical request",
            Department = requester.Department
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteUnusedAsync(id, "DELETE", DemoDataSeeder.AdminId);
        Assert.False(result.Succeeded);
        Assert.Contains("Deactivate", result.Error);
        Assert.True(await db.DocumentTypes.AnyAsync(x => x.Id == id));
    }

    private static NewDocumentType Definition(string key, string prefix) => new(
        "Test " + key,
        key,
        "Test document type.",
        prefix,
        [new NewDocumentField("purpose", "Purpose", DocumentFieldType.LongText, true, "Describe the request.", null)],
        [new NewDocumentTypeAccess(DemoDataSeeder.PurchasingId, DocumentTypeAccessRole.Administrator)],
        [new NewLifecycleNotificationRule(
            LifecycleNotificationEvent.RequestCompleted,
            LifecycleNotificationRecipient.DocumentTypeAdministrators,
            null,
            null,
            true,
            false,
            false)]);
}
