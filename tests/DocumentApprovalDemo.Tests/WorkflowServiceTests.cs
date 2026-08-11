using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task Workflow_RequiresMatchingSignature_AndCompletesInConfiguredOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        var request = new ApprovalRequest
        {
            RequestNumber = "PR-TEST-0001", RequesterId = requester.Id, Requester = requester,
            ConfirmedManagerId = DemoDataSeeder.ManagerId, Title = "Test purchase",
            Subcategory = "One-Time Purchase", Vendor = "Test Vendor", Department = requester.Department,
            Amount = 500m, BusinessJustification = "Exercise the complete approval path."
        };
        request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1 });
        db.Requests.Add(request);
        await db.SaveChangesAsync();

        var service = new WorkflowService(db, new RoutingService(db), new NoOpNotificationService());
        await service.StartAsync(request, requester);

        Assert.Equal(new[] { "Manager", "VP Finance" }, request.Approvals.OrderBy(x => x.Sequence).Select(x => x.StageName));
        var managerApproval = request.Approvals.Single(x => x.StageName == "Manager");
        var badSignature = await service.DecideAsync(managerApproval.Id, DemoDataSeeder.ManagerId, DecisionType.Approve, "Wrong Name", null);
        Assert.False(badSignature.Succeeded);
        Assert.Equal(ApprovalStatus.Pending, managerApproval.Status);

        var managerDecision = await service.DecideAsync(managerApproval.Id, DemoDataSeeder.ManagerId, DecisionType.Approve, "Morgan Manager", "Approved");
        Assert.True(managerDecision.Succeeded);
        var financeApproval = request.Approvals.Single(x => x.StageName == "VP Finance");
        Assert.Equal(ApprovalStatus.Pending, financeApproval.Status);

        var financeDecision = await service.DecideAsync(financeApproval.Id, DemoDataSeeder.FinanceId, DecisionType.Approve, "Finley Finance", null);
        Assert.True(financeDecision.Succeeded);
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.All(request.Approvals, x => Assert.Equal(ApprovalStatus.Approved, x.Status));
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task QueueApprovalAssignedAsync(ApplicationUser user, ApprovalRequest request, string stageName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task QueueRequestOutcomeAsync(ApplicationUser user, ApprovalRequest request, string outcome, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
