using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class ApprovalRecordServiceTests
{
    [Fact]
    public void CreateModel_ContainsCurrentDynamicDataEvidenceAttachmentsAndSkippedStages()
    {
        var requester = new ApplicationUser { FullName = "Avery Employee", Email = "avery@example.org" };
        var approver = new ApplicationUser { FullName = "Morgan Manager", Email = "morgan@example.org" };
        var route = new ApprovalRouteVersion { VersionNumber = 1, Name = "Policy governance" };
        var managerStage = new ApprovalRouteStage { RouteVersion = route, Sequence = 1, Name = "Manager Review", StageKey = "manager" };
        var conditionalStage = new ApprovalRouteStage { RouteVersion = route, Sequence = 2, Name = "Compliance Review", StageKey = "compliance", IsConditional = true };
        var root = new RouteConditionGroup { Stage = conditionalStage, StableGroupKey = "root", Sequence = 1 };
        var rule = new RouteConditionRule { Group = root, StableRuleKey = "risk", Sequence = 1, FieldKey = "risk_level", Operator = ComparisonOperator.Equals };
        rule.Operands.Add(new RouteConditionOperand { Rule = rule, Sequence = 1, Value = "Critical" });
        root.Rules.Add(rule);
        conditionalStage.ConditionGroups.Add(root);
        route.Stages.Add(managerStage);
        route.Stages.Add(conditionalStage);
        var request = new ApprovalRequest
        {
            RequestNumber = "POL-2026-0001", Title = "Records policy", Requester = requester,
            Department = "Operations", DocumentType = new DocumentType { Name = "Policy Approval" },
            Status = RequestStatus.Approved, CurrentRevisionNumber = 1,
            RouteVersion = route,
            SubmittedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        request.DocumentType.Fields.Add(new DocumentFieldDefinition { Key = "risk_level", Label = "Risk level", FieldType = DocumentFieldType.Choice });
        request.FieldValues.Add(new RequestFieldValue { RevisionNumber = 1, Sequence = 1, Label = "Risk level", FieldKey = "risk_level", Value = "High" });
        request.Approvals.Add(new ApprovalInstance
        {
            RevisionNumber = 1, RouteStageId = managerStage.Id, Sequence = 1, StageName = "Manager Review", Approver = approver,
            Status = ApprovalStatus.Approved,
            Decision = new ApprovalDecision
            {
                Decision = DecisionType.Approve, TypedSignature = approver.FullName,
                AuthenticatedFullName = approver.FullName, AuthenticatedEmail = approver.Email,
                DecidedAtUtc = DateTimeOffset.UtcNow
            }
        });
        request.Attachments.Add(new RequestAttachment
        {
            RevisionNumber = 1,
            OriginalFileName = "policy.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024
        });

        var service = new ApprovalRecordService();
        var model = service.CreateModel(request,
        [
            new AuditEvent { EventType = "RequestSubmitted", Details = "Submitted.", OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-1) }
        ]);

        Assert.Equal("Policy Approval", model.DocumentType);
        Assert.Contains(model.RequestValues, x => x.Label == "Risk level" && x.Value == "High");
        Assert.Contains(model.Approvals, x => x.Stage == "Manager Review" && x.Signature == "Morgan Manager");
        Assert.Contains(model.Approvals, x => x.Stage == "Compliance Review" && x.Status == "Not Required / Skipped");
        Assert.Contains(model.Attachments, x => x.FileName == "policy.pdf" && x.Revision == 1);
        Assert.Single(model.History);

        var pdf = service.Build(request);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
