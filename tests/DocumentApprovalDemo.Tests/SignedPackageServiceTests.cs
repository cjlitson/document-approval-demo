using System.Text;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class SignedPackageServiceTests
{
    [Fact]
    public void Build_ReturnsPdfContainingApprovalEvidence()
    {
        var requester = new ApplicationUser { FullName = "Avery Employee", Email = "avery@example.org" };
        var approver = new ApplicationUser { FullName = "Morgan Manager", Email = "morgan@example.org" };
        var request = new ApprovalRequest
        {
            RequestNumber = "PR-2026-0001", Title = "Example purchase", Requester = requester,
            Department = "Operations", Subcategory = "One-Time Purchase", Vendor = "Example Vendor",
            Amount = 1250m, BusinessJustification = "Needed for the documented business process.",
            Status = RequestStatus.Approved, CurrentRevisionNumber = 1,
            RouteVersion = new ApprovalRouteVersion { VersionNumber = 1 }
        };
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 1, Sequence = 1, StageName = "Manager", Approver = approver,
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
        Assert.Contains("Morgan Manager", text);
        Assert.Contains("Adopted signature", text);
        Assert.EndsWith("%%EOF", text);
    }
}
