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
}
