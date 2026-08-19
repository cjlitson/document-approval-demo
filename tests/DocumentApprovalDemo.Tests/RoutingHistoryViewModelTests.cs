using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.ViewModels;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class RoutingHistoryViewModelTests
{
    [Fact]
    public void Create_UsesCurrentRequestAndApprovalEvidence()
    {
        var requester = new ApplicationUser { FullName = "Avery Employee" };
        var manager = new ApplicationUser { FullName = "Morgan Manager" };
        var president = new ApplicationUser { FullName = "Pat President" };
        var finance = new ApplicationUser { FullName = "Finley Finance" };
        var request = new ApprovalRequest
        {
            Requester = requester,
            DocumentType = new DocumentType { Name = "Purchase Request" },
            RouteVersion = new ApprovalRouteVersion { VersionNumber = 3 },
            CurrentRevisionNumber = 2,
            Status = RequestStatus.InApproval,
            SubmittedAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
        };
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 2,
            Sequence = 1,
            StageName = "Manager Review",
            Approver = manager,
            Status = ApprovalStatus.Approved,
            CompletedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            Decision = new ApprovalDecision
            {
                TypedSignature = manager.FullName,
                Comments = "Approved as submitted."
            }
        });
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 2,
            Sequence = 2,
            StageName = "Executive Review",
            Approver = president,
            Status = ApprovalStatus.Pending,
            ActivatedAtUtc = DateTimeOffset.UtcNow.AddHours(-4)
        });
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 2,
            Sequence = 3,
            StageName = "Financial Control Review",
            Approver = finance,
            Status = ApprovalStatus.Queued
        });

        var result = RoutingHistoryViewModel.Create(request);

        Assert.Equal(2, result.RevisionNumber);
        Assert.Equal(3, result.RouteVersionNumber);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal("completed", result.Items[0].State);
        Assert.Equal("completed", result.Items[1].State);
        Assert.Equal("Morgan Manager", result.Items[1].Signature);
        Assert.Equal("Routed to Executive Review.", result.Items[1].Transition);
        Assert.Equal("current", result.Items[2].State);
        Assert.Equal("future", result.Items[3].State);
        Assert.Equal("Workflow completed", result.Items[4].Title);
        Assert.Equal("Pending", result.Items[4].StatusLabel);
    }

    [Fact]
    public void WorkflowProgress_DistinguishesCompletedCurrentSkippedAndFuture()
    {
        var route = new ApprovalRouteVersion { Name = "Test route", VersionNumber = 1 };
        var first = new ApprovalRouteStage { RouteVersion = route, Sequence = 1, Name = "Manager", StageKey = "manager" };
        var skipped = new ApprovalRouteStage { RouteVersion = route, Sequence = 2, Name = "Executive", StageKey = "executive", IsConditional = true };
        var future = new ApprovalRouteStage { RouteVersion = route, Sequence = 3, Name = "Finance", StageKey = "finance" };
        route.Stages.Add(first);
        route.Stages.Add(skipped);
        route.Stages.Add(future);
        var manager = new ApplicationUser { FullName = "Morgan Manager" };
        var finance = new ApplicationUser { FullName = "Finley Finance" };
        var request = new ApprovalRequest
        {
            Requester = new ApplicationUser { FullName = "Avery Employee" },
            RouteVersion = route,
            CurrentRevisionNumber = 1,
            Status = RequestStatus.InApproval,
            SubmittedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
        };
        request.Approvals.Add(new ApprovalInstance
        {
            RouteStageId = first.Id, RevisionNumber = 1, Sequence = 1, StageName = first.Name,
            Approver = manager, Status = ApprovalStatus.Approved
        });
        request.Approvals.Add(new ApprovalInstance
        {
            RouteStageId = future.Id, RevisionNumber = 1, Sequence = 3, StageName = future.Name,
            Approver = finance, Status = ApprovalStatus.Pending
        });

        var result = WorkflowProgressViewModel.Create(request);

        Assert.Equal("completed", result.Items.Single(x => x.Title == "Manager").State);
        Assert.Equal("skipped", result.Items.Single(x => x.Title == "Executive").State);
        Assert.Equal("current", result.Items.Single(x => x.Title == "Finance").State);
        Assert.Equal("future", result.Items.Single(x => x.Title == "Completed").State);
    }
}
