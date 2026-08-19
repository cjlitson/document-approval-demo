using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface ILifecycleNotificationService
{
    Task QueueAsync(
        LifecycleNotificationEvent eventType,
        ApprovalRequest request,
        ApprovalRouteStage? stage = null,
        ApprovalInstance? currentApproval = null,
        CancellationToken cancellationToken = default);
}

public sealed class LifecycleNotificationService(AppDbContext db) : ILifecycleNotificationService
{
    public async Task QueueAsync(
        LifecycleNotificationEvent eventType,
        ApprovalRequest request,
        ApprovalRouteStage? stage = null,
        ApprovalInstance? currentApproval = null,
        CancellationToken cancellationToken = default)
    {
        var rules = await db.LifecycleNotificationRules.AsNoTracking()
            .Where(x => x.DocumentTypeId == request.DocumentTypeId && x.IsEnabled && x.EventType == eventType)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules.Where(x => StageMatches(x, stage)))
        {
            var recipientIds = await ResolveRecipientsAsync(rule, request, currentApproval, cancellationToken);
            foreach (var recipientId in recipientIds.Distinct())
            {
                foreach (var channel in EnabledChannels(rule))
                {
                    var stageKey = stage?.StageKey ?? "request";
                    var key = $"lifecycle:{rule.Id:N}:{request.Id:N}:r{request.CurrentRevisionNumber}:{stageKey}:{recipientId:N}:{channel}:{eventType}";
                    if (db.NotificationOutbox.Local.Any(x => x.IdempotencyKey == key) ||
                        await db.NotificationOutbox.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
                        continue;

                    db.NotificationOutbox.Add(new NotificationOutbox
                    {
                        UserId = recipientId,
                        RequestId = request.Id,
                        ApprovalInstanceId = null,
                        AlertPolicyId = null,
                        LifecycleNotificationRuleId = rule.Id,
                        EventType = AlertEventType.Outcome,
                        LifecycleEventType = eventType,
                        Channel = channel,
                        Subject = BuildSubject(eventType, request, stage),
                        Body = BuildBody(eventType, request, stage),
                        ActionUrl = $"/requests/{request.Id}",
                        IdempotencyKey = key,
                        DueAtUtc = DateTimeOffset.UtcNow.AddHours(rule.DelayHours),
                        MaxAttempts = 3,
                        Status = NotificationStatus.Pending
                    });
                }
            }
        }
    }

    private static bool StageMatches(LifecycleNotificationRule rule, ApprovalRouteStage? stage)
    {
        if (rule.EventType is not (LifecycleNotificationEvent.StageStarted or LifecycleNotificationEvent.StageCompleted))
            return true;
        if (string.IsNullOrWhiteSpace(rule.StageKey)) return true;
        return stage is not null && string.Equals(rule.StageKey, stage.StageKey, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(
        LifecycleNotificationRule rule,
        ApprovalRequest request,
        ApprovalInstance? currentApproval,
        CancellationToken cancellationToken)
    {
        IEnumerable<Guid> candidates = rule.RecipientType switch
        {
            LifecycleNotificationRecipient.Requester => [request.RequesterId],
            LifecycleNotificationRecipient.RequesterManager => [request.ConfirmedManagerId],
            LifecycleNotificationRecipient.CurrentApprover when currentApproval is not null => [currentApproval.ApproverId],
            LifecycleNotificationRecipient.NamedUser when rule.NamedUserId.HasValue => [rule.NamedUserId.Value],
            LifecycleNotificationRecipient.UserFromRequestField => ResolveUserField(request, rule.UserFieldKey),
            LifecycleNotificationRecipient.DocumentTypeAdministrators => await AccessRecipientsAsync(
                request.DocumentTypeId, DocumentTypeAccessRole.Administrator, cancellationToken),
            LifecycleNotificationRecipient.DocumentTypeCoordinators => await AccessRecipientsAsync(
                request.DocumentTypeId, DocumentTypeAccessRole.Coordinator, cancellationToken),
            _ => []
        };

        var ids = candidates.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private Task<List<Guid>> AccessRecipientsAsync(
        Guid documentTypeId,
        DocumentTypeAccessRole role,
        CancellationToken cancellationToken) =>
        db.DocumentTypeAccess.AsNoTracking()
            .Where(x => x.DocumentTypeId == documentTypeId && x.AccessRole == role && x.IsActive && x.User.IsActive)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

    private static IEnumerable<Guid> ResolveUserField(ApprovalRequest request, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return [];
        return Guid.TryParse(request.GetFieldValue(key), out var id) ? [id] : [];
    }

    private static IEnumerable<NotificationChannel> EnabledChannels(LifecycleNotificationRule rule)
    {
        if (rule.SendInApp) yield return NotificationChannel.InApp;
        if (rule.SendEmail) yield return NotificationChannel.Email;
        if (rule.SendTeams) yield return NotificationChannel.Teams;
    }

    private static string BuildSubject(
        LifecycleNotificationEvent eventType,
        ApprovalRequest request,
        ApprovalRouteStage? stage) => eventType switch
        {
            LifecycleNotificationEvent.RequestSubmitted => $"Submitted: {request.RequestNumber}",
            LifecycleNotificationEvent.StageStarted => $"Stage started: {request.RequestNumber}",
            LifecycleNotificationEvent.StageCompleted => $"Stage completed: {request.RequestNumber}",
            LifecycleNotificationEvent.RequestRejected => $"Rejected: {request.RequestNumber}",
            LifecycleNotificationEvent.RequestCompleted => $"Completed: {request.RequestNumber}",
            _ => request.RequestNumber
        };

    private static string BuildBody(
        LifecycleNotificationEvent eventType,
        ApprovalRequest request,
        ApprovalRouteStage? stage)
    {
        var typeAndTitle = $"{request.DocumentType.Name} {request.RequestNumber} — {request.Title}";
        return eventType switch
        {
            LifecycleNotificationEvent.RequestSubmitted => $"{typeAndTitle} was submitted for approval.",
            LifecycleNotificationEvent.StageStarted => $"{typeAndTitle} entered {stage?.Name ?? "the next workflow stage"}.",
            LifecycleNotificationEvent.StageCompleted => $"{typeAndTitle} completed {stage?.Name ?? "a workflow stage"}.",
            LifecycleNotificationEvent.RequestRejected => $"{typeAndTitle} was rejected and requires revision.",
            LifecycleNotificationEvent.RequestCompleted => $"{typeAndTitle} has completed approval and is ready for operational follow-up.",
            _ => typeAndTitle
        };
    }
}

internal sealed class NullLifecycleNotificationService : ILifecycleNotificationService
{
    public Task QueueAsync(
        LifecycleNotificationEvent eventType,
        ApprovalRequest request,
        ApprovalRouteStage? stage = null,
        ApprovalInstance? currentApproval = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
