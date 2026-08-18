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
    public async Task Workflow_RequiresMatchingSignature_AndCompletesConfiguredStages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        var documentType = await db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PurchaseDocumentTypeId);
        var amountField = await db.DocumentFields.SingleAsync(x => x.DocumentTypeId == documentType.Id && x.Key == "amount");
        var request = new ApprovalRequest
        {
            RequestNumber = "PUR-TEST-0001", DocumentTypeId = documentType.Id, DocumentType = documentType,
            RequesterId = requester.Id, Requester = requester, ConfirmedManagerId = DemoDataSeeder.ManagerId,
            Title = "Test purchase", Department = requester.Department
        };
        request.FieldValues.Add(new RequestFieldValue { Request = request, FieldDefinitionId = amountField.Id, RevisionNumber = 1, FieldKey = "amount", Label = "Amount", FieldType = DocumentFieldType.Currency, Value = "500" });
        request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1 });
        db.Requests.Add(request);
        await db.SaveChangesAsync();

        var service = new WorkflowService(db, new RoutingService(db), new NoOpNotificationService());
        await service.StartAsync(request, requester);

        Assert.Equal(new[] { "Manager Review", "Financial Control Review" }, request.Approvals.OrderBy(x => x.Sequence).Select(x => x.StageName));
        var managerApproval = request.Approvals.Single(x => x.StageName == "Manager Review");
        var badSignature = await service.DecideAsync(managerApproval.Id, DemoDataSeeder.ManagerId, DecisionType.Approve, "Wrong Name", null);
        Assert.False(badSignature.Succeeded);
        Assert.Equal(ApprovalStatus.Pending, managerApproval.Status);

        var managerDecision = await service.DecideAsync(managerApproval.Id, DemoDataSeeder.ManagerId, DecisionType.Approve, "Morgan Manager", "Approved");
        Assert.True(managerDecision.Succeeded);
        var finalApproval = request.Approvals.Single(x => x.StageName == "Financial Control Review");
        Assert.Equal(ApprovalStatus.Pending, finalApproval.Status);

        var finalDecision = await service.DecideAsync(finalApproval.Id, DemoDataSeeder.FinanceId, DecisionType.Approve, "Finley Finance", null);
        Assert.True(finalDecision.Succeeded);
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.All(request.Approvals, x => Assert.Equal(ApprovalStatus.Approved, x.Status));
    }

    [Fact]
    public async Task PolicyWorkflow_CanAssignFinalStageFromUserField()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        var documentType = await db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PolicyDocumentTypeId);
        var riskField = await db.DocumentFields.SingleAsync(x => x.DocumentTypeId == documentType.Id && x.Key == "risk_level");
        var ownerField = await db.DocumentFields.SingleAsync(x => x.DocumentTypeId == documentType.Id && x.Key == "records_approver");
        var request = new ApprovalRequest { RequestNumber = "POL-TEST-0001", DocumentType = documentType, DocumentTypeId = documentType.Id, Requester = requester, RequesterId = requester.Id, ConfirmedManagerId = DemoDataSeeder.ManagerId, Title = "Records policy", Department = requester.Department };
        request.FieldValues.Add(new RequestFieldValue { Request = request, FieldDefinitionId = riskField.Id, RevisionNumber = 1, FieldKey = "risk_level", Label = "Risk level", Value = "Low" });
        request.FieldValues.Add(new RequestFieldValue { Request = request, FieldDefinitionId = ownerField.Id, RevisionNumber = 1, FieldKey = "records_approver", Label = "Records approver", FieldType = DocumentFieldType.User, Value = DemoDataSeeder.AdminId.ToString() });
        request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1 });
        db.Requests.Add(request); await db.SaveChangesAsync();

        var service = new WorkflowService(db, new RoutingService(db), new NoOpNotificationService());
        await service.StartAsync(request, requester);

        Assert.Equal(new[] { "Owner Manager Review", "Records Approval" }, request.Approvals.OrderBy(x => x.Sequence).Select(x => x.StageName));
        Assert.Equal(DemoDataSeeder.AdminId, request.Approvals.Single(x => x.StageName == "Records Approval").ApproverId);
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task QueueStageAlertsAsync(ApplicationUser approver, ApprovalRequest request, ApprovalInstance approval, ApprovalRouteStage stage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task QueueRequestOutcomeAsync(ApplicationUser requester, ApprovalRequest request, ApprovalInstance approval, ApprovalRouteStage stage, string outcome, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelPendingForApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
