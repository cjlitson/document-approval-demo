using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class RouteVersionBindingTests
{
    [Fact]
    public async Task PublishingNewVersion_DoesNotRebindExistingRequest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();

        var source = await db.RouteVersions
            .Include(x => x.Route)
            .Include(x => x.Stages).ThenInclude(x => x.ConditionGroups).ThenInclude(x => x.Rules).ThenInclude(x => x.Operands)
            .Include(x => x.Stages).ThenInclude(x => x.AlertPolicies)
            .SingleAsync(x => x.Route.DocumentTypeId == DemoDataSeeder.PurchaseDocumentTypeId && x.Status == RouteVersionStatus.Published);
        var request = new ApprovalRequest
        {
            RequestNumber = "PUR-BIND-0001",
            DocumentTypeId = DemoDataSeeder.PurchaseDocumentTypeId,
            RequesterId = DemoDataSeeder.EmployeeId,
            ConfirmedManagerId = DemoDataSeeder.ManagerId,
            Title = "Version-bound request",
            Department = "Operations",
            RouteVersionId = source.Id,
            Status = RequestStatus.InApproval
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync();

        var next = new RouteVersionCloningService().CloneAsDraft(source);
        next.Status = RouteVersionStatus.Published;
        next.PublishedAtUtc = DateTimeOffset.UtcNow;
        source.Status = RouteVersionStatus.Retired;
        db.RouteVersions.Add(next);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persistedRequest = await db.Requests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        var live = await new RoutingService(db).GetPublishedRouteAsync(DemoDataSeeder.PurchaseDocumentTypeId);

        Assert.Equal(source.Id, persistedRequest.RouteVersionId);
        Assert.Equal(next.Id, live.Id);
        Assert.Equal(RouteVersionStatus.Retired, await db.RouteVersions.Where(x => x.Id == source.Id).Select(x => x.Status).SingleAsync());
    }
}
