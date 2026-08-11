using System.Text;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class SignedPackageServiceTests
{
    [Fact]
    public void Build_ReturnsPdfContainingDynamicDocumentDataAndApprovalEvidence()
    {
        var requester = new ApplicationUser { FullName = "Avery Employee", Email = "avery@example.org" };
        var approver = new ApplicationUser { FullName = "Morgan Manager", Email = "morgan@example.org" };
        var request = new ApprovalRequest
        {
            RequestNumber = "POL-2026-0001", Title = "Records policy", Requester = requester,
            Department = "Operations", DocumentType = new DocumentType { Name = "Policy Approval" },
            Status = RequestStatus.Approved, CurrentRevisionNumber = 1,
            RouteVersion = new ApprovalRouteVersion { VersionNumber = 1 }
        };
        request.FieldValues.Add(new RequestFieldValue { RevisionNumber = 1, Sequence = 1, Label = "Risk level", FieldKey = "risk_level", Value = "High" });
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 1, Sequence = 1, StageName = "Manager Review", Approver = approver,
            Status = ApprovalStatus.Approved,
            Decision = new ApprovalDecision
            {
                Decision = DecisionType.Approve, TypedSignature = approver.FullName,
                AuthenticatedFullName = approver.FullName, AuthenticatedEmail = approver.Email
            }
        });

        var result = new SignedPackageService().Build(request);
        var text = Encoding.Latin1.GetString(result);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("Policy Approval", text);
        Assert.Contains("Risk level: High", text);
        Assert.Contains("Morgan Manager", text);
        Assert.Contains("Adopted signature", text);
        Assert.EndsWith("%%EOF", text);
    }
}
