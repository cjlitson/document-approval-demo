using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public sealed record DecisionResult(bool Succeeded, string? Error = null);

public interface IWorkflowService
{
    Task StartAsync(ApprovalRequest request, ApplicationUser actor, CancellationToken cancellationToken = default);
    Task<DecisionResult> DecideAsync(Guid approvalId, Guid actorId, DecisionType decision, string typedSignature, string? comments, CancellationToken cancellationToken = default);
    Task RestartAsync(ApprovalRequest request, ApplicationUser actor, string changeSummary, CancellationToken cancellationToken = default);
}

public sealed class WorkflowService(
    AppDbContext db,
    IRoutingService routing,
    INotificationService notifications) : IWorkflowService
{
    public async Task StartAsync(ApprovalRequest request, ApplicationUser actor, CancellationToken cancellationToken = default)
    {
        var route = await routing.GetPublishedPurchaseRouteAsync(cancellationToken);
        request.RouteVersionId = route.Id;
        request.Status = RequestStatus.InApproval;
        request.SubmittedAtUtc = DateTimeOffset.UtcNow;

        var stages = route.Stages.OrderBy(x => x.Sequence).Where(x => routing.ShouldIncludeStage(x, request)).ToList();
        if (stages.Count == 0) throw new InvalidOperationException("The published route has no applicable stages.");

        foreach (var stage in stages)
        {
            var approverId = stage.AssignmentType == "RequesterManager"
                ? request.ConfirmedManagerId
                : stage.NamedApproverId ?? throw new InvalidOperationException($"No approver is configured for {stage.Name}.");
            var instance = new ApprovalInstance
            {
                Request = request,
                RevisionNumber = request.CurrentRevisionNumber,
                RouteVersionId = route.Id,
                RouteStageId = stage.Id,
                Sequence = stage.Sequence,
                StageName = stage.Name,
                ApproverId = approverId,
                Status = ApprovalStatus.Queued
            };
            request.Approvals.Add(instance);
            db.ApprovalInstances.Add(instance);
        }

        ActivateFirst(request);
        db.AuditEvents.Add(Audit(request.Id, actor.Id, "RequestSubmitted",
            $"Revision {request.CurrentRevisionNumber} submitted with route version {route.VersionNumber}."));
        await db.SaveChangesAsync(cancellationToken);

        var pending = request.Approvals.Single(x => x.RevisionNumber == request.CurrentRevisionNumber && x.Status == ApprovalStatus.Pending);
        var approver = await db.Users.SingleAsync(x => x.Id == pending.ApproverId, cancellationToken);
        await notifications.QueueApprovalAssignedAsync(approver, request, pending.StageName, cancellationToken);
    }

    public async Task<DecisionResult> DecideAsync(Guid approvalId, Guid actorId, DecisionType decision, string typedSignature, string? comments, CancellationToken cancellationToken = default)
    {
        var approval = await db.ApprovalInstances
            .Include(x => x.Request).ThenInclude(x => x.Requester)
            .Include(x => x.Approver)
            .SingleOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (approval is null) return new(false, "Approval was not found.");
        if (approval.Status != ApprovalStatus.Pending) return new(false, "This approval is no longer pending.");
        if (approval.ApproverId != actorId) return new(false, "This approval is assigned to another user.");

        var normalizedTyped = typedSignature.Trim();
        if (!string.Equals(normalizedTyped, approval.Approver.FullName.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(false, $"Type your full name exactly as shown: {approval.Approver.FullName}.");
        if (decision == DecisionType.Reject && string.IsNullOrWhiteSpace(comments))
            return new(false, "Comments are required when rejecting a request.");

        var now = DateTimeOffset.UtcNow;
        approval.Status = decision == DecisionType.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        approval.CompletedAtUtc = now;
        approval.Decision = new ApprovalDecision
        {
            Decision = decision,
            TypedSignature = normalizedTyped,
            AuthenticatedFullName = approval.Approver.FullName,
            AuthenticatedEmail = approval.Approver.Email,
            AuthenticationMethod = "Authenticated demo cookie (Microsoft Entra ID in production)",
            Comments = comments?.Trim(),
            DecidedAtUtc = now
        };
        db.ApprovalDecisions.Add(approval.Decision);
        db.AuditEvents.Add(Audit(approval.RequestId, actorId, "ApprovalDecision",
            $"{approval.StageName} {decision} for revision {approval.RevisionNumber}."));

        if (decision == DecisionType.Reject)
        {
            approval.Request.Status = RequestStatus.Rejected;
            foreach (var queued in await db.ApprovalInstances.Where(x => x.RequestId == approval.RequestId && x.RevisionNumber == approval.RevisionNumber && x.Status == ApprovalStatus.Queued).ToListAsync(cancellationToken))
                queued.Status = ApprovalStatus.Superseded;
            var revision = await db.RequestRevisions.SingleAsync(x => x.RequestId == approval.RequestId && x.RevisionNumber == approval.RevisionNumber, cancellationToken);
            revision.Status = RevisionStatus.Rejected;
            await db.SaveChangesAsync(cancellationToken);
            await notifications.QueueRequestOutcomeAsync(approval.Request.Requester, approval.Request, "Rejected", cancellationToken);
            return new(true);
        }

        var next = await db.ApprovalInstances
            .Where(x => x.RequestId == approval.RequestId && x.RevisionNumber == approval.RevisionNumber && x.Status == ApprovalStatus.Queued)
            .OrderBy(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is not null)
        {
            next.Status = ApprovalStatus.Pending;
            next.ActivatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            var nextApprover = await db.Users.SingleAsync(x => x.Id == next.ApproverId, cancellationToken);
            await notifications.QueueApprovalAssignedAsync(nextApprover, approval.Request, next.StageName, cancellationToken);
        }
        else
        {
            approval.Request.Status = RequestStatus.Approved;
            approval.Request.CompletedAtUtc = now;
            var revision = await db.RequestRevisions.SingleAsync(x => x.RequestId == approval.RequestId && x.RevisionNumber == approval.RevisionNumber, cancellationToken);
            revision.Status = RevisionStatus.Approved;
            db.AuditEvents.Add(Audit(approval.RequestId, actorId, "RequestApproved", "All required stages approved; signed package is available."));
            await db.SaveChangesAsync(cancellationToken);
            await notifications.QueueRequestOutcomeAsync(approval.Request.Requester, approval.Request, "Approved", cancellationToken);
        }

        return new(true);
    }

    public async Task RestartAsync(ApprovalRequest request, ApplicationUser actor, string changeSummary, CancellationToken cancellationToken = default)
    {
        var oldRevision = request.CurrentRevisionNumber;
        foreach (var instance in request.Approvals.Where(x => x.RevisionNumber == oldRevision && x.Status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Pending or ApprovalStatus.Queued))
            instance.Status = ApprovalStatus.Superseded;
        var prior = request.Revisions.Single(x => x.RevisionNumber == oldRevision);
        prior.Status = RevisionStatus.Superseded;

        request.CurrentRevisionNumber++;
        request.Status = RequestStatus.InApproval;
        request.CompletedAtUtc = null;
        var revision = new RequestRevision
        {
            Request = request,
            RevisionNumber = request.CurrentRevisionNumber,
            ChangeSummary = changeSummary.Trim(),
            Status = RevisionStatus.InApproval
        };
        request.Revisions.Add(revision);
        db.RequestRevisions.Add(revision);
        db.AuditEvents.Add(Audit(request.Id, actor.Id, "RequestRevised",
            $"Revision {request.CurrentRevisionNumber} created; approval restarted at stage one."));
        await db.SaveChangesAsync(cancellationToken);
        await StartAsync(request, actor, cancellationToken);
    }

    private static void ActivateFirst(ApprovalRequest request)
    {
        var first = request.Approvals
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber && x.Status == ApprovalStatus.Queued)
            .OrderBy(x => x.Sequence)
            .First();
        first.Status = ApprovalStatus.Pending;
        first.ActivatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static AuditEvent Audit(Guid requestId, Guid actorId, string type, string details) => new()
    {
        RequestId = requestId, ActorUserId = actorId, EventType = type, Details = details
    };
}
