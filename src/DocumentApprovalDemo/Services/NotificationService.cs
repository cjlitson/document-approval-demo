using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface INotificationService
{
    Task QueueStageAlertsAsync(
        ApplicationUser approver,
        ApprovalRequest request,
        ApprovalInstance approval,
        ApprovalRouteStage stage,
        CancellationToken cancellationToken = default);

    Task QueueRequestOutcomeAsync(
        ApplicationUser requester,
        ApprovalRequest request,
        ApprovalInstance approval,
        ApprovalRouteStage stage,
        string outcome,
        CancellationToken cancellationToken = default);

    Task CancelPendingForApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default);
}

public sealed class OutboxNotificationService(AppDbContext db) : INotificationService
{
    public async Task QueueStageAlertsAsync(
        ApplicationUser approver,
        ApprovalRequest request,
        ApprovalInstance approval,
        ApprovalRouteStage stage,
        CancellationToken cancellationToken = default)
    {
        var policies = stage.AlertPolicies
            .Where(x => x.IsEnabled && x.EventType is AlertEventType.Assignment or AlertEventType.Reminder or AlertEventType.Escalation)
            .OrderBy(x => x.DelayHours)
            .ToList();

        foreach (var policy in policies)
        {
            var recipient = await ResolveRecipientAsync(policy.RecipientStrategy, approver, request, cancellationToken);
            var dueAt = (approval.ActivatedAtUtc ?? DateTimeOffset.UtcNow).AddHours(policy.DelayHours);
            var subject = policy.EventType switch
            {
                AlertEventType.Assignment => $"Approval needed: {request.RequestNumber}",
                AlertEventType.Reminder => $"Reminder: {request.RequestNumber} is waiting",
                AlertEventType.Escalation => $"Escalation: {request.RequestNumber} is overdue",
                _ => request.RequestNumber
            };
            var body = policy.EventType switch
            {
                AlertEventType.Assignment => $"{stage.Name} is ready for '{request.Title}'. Open the application to review and sign.",
                AlertEventType.Reminder => $"{stage.Name} for '{request.Title}' is still waiting for {approver.FullName}.",
                AlertEventType.Escalation => $"{stage.Name} for '{request.Title}' has been waiting {policy.DelayHours} hours. The assigned approver is {approver.FullName}.",
                _ => ""
            };
            var url = policy.EventType == AlertEventType.Escalation
                ? $"/requests/{request.Id}"
                : $"/approvals/{approval.Id}";
            await AddChannelsAsync(policy, recipient, request, approval, subject, body, url, outcomeKey: null, dueAt, cancellationToken);
        }
    }

    public async Task QueueRequestOutcomeAsync(
        ApplicationUser requester,
        ApprovalRequest request,
        ApprovalInstance approval,
        ApprovalRouteStage stage,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        foreach (var policy in stage.AlertPolicies.Where(x => x.IsEnabled && x.EventType == AlertEventType.Outcome))
        {
            await AddChannelsAsync(
                policy,
                requester,
                request,
                approval,
                $"{request.RequestNumber} {outcome}",
                $"Your {request.DocumentType.Name.ToLowerInvariant()} '{request.Title}' is now {outcome.ToLowerInvariant()}.",
                $"/requests/{request.Id}",
                outcome,
                DateTimeOffset.UtcNow.AddHours(policy.DelayHours),
                cancellationToken);
        }
    }

    public async Task CancelPendingForApprovalAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        var pending = await db.NotificationOutbox
            .Where(x => x.ApprovalInstanceId == approvalId && x.Status == NotificationStatus.Pending &&
                        (x.EventType == AlertEventType.Reminder || x.EventType == AlertEventType.Escalation))
            .ToListAsync(cancellationToken);
        foreach (var notification in pending) notification.Status = NotificationStatus.Cancelled;
    }

    private async Task<ApplicationUser> ResolveRecipientAsync(
        AlertRecipientStrategy strategy,
        ApplicationUser approver,
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (strategy == AlertRecipientStrategy.Requester)
            return await db.Users.SingleAsync(x => x.Id == request.RequesterId, cancellationToken);
        if (strategy == AlertRecipientStrategy.StageApprover) return approver;
        if (approver.ManagerId is { } managerId)
            return await db.Users.SingleAsync(x => x.Id == managerId, cancellationToken);
        return await db.Users.OrderBy(x => x.FullName)
            .FirstAsync(x => x.IsActive && x.RolesCsv.Contains(Roles.SystemAdmin), cancellationToken);
    }

    private async Task AddChannelsAsync(
        AlertPolicy policy,
        ApplicationUser recipient,
        ApprovalRequest request,
        ApprovalInstance approval,
        string subject,
        string body,
        string actionUrl,
        string? outcomeKey,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var channels = new List<NotificationChannel>();
        if (policy.InAppEnabled) channels.Add(NotificationChannel.InApp);
        if (policy.EmailEnabled) channels.Add(NotificationChannel.Email);
        if (policy.TeamsEnabled) channels.Add(NotificationChannel.Teams);

        foreach (var channel in channels)
        {
            var key = $"{policy.Id:N}:{approval.Id:N}:{recipient.Id:N}:{channel}:{outcomeKey ?? "stage"}";
            if (db.NotificationOutbox.Local.Any(x => x.IdempotencyKey == key) ||
                await db.NotificationOutbox.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
                continue;

            db.NotificationOutbox.Add(new NotificationOutbox
            {
                UserId = recipient.Id,
                RequestId = request.Id,
                ApprovalInstanceId = approval.Id,
                AlertPolicyId = policy.Id,
                EventType = policy.EventType,
                Channel = channel,
                Subject = subject,
                Body = body,
                ActionUrl = actionUrl,
                IdempotencyKey = key,
                DueAtUtc = dueAt,
                MaxAttempts = policy.MaxDeliveryAttempts,
                Status = NotificationStatus.Pending
            });
        }
    }
}

public interface INotificationDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken = default);
}

public sealed class SimulatedNotificationDispatcher(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<SimulatedNotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.NotificationOutbox.AsNoTracking()
            .Where(x => x.Status == NotificationStatus.Pending)
            .ToListAsync(cancellationToken);
        var due = candidates
            .Where(x => x.DueAtUtc <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.DueAtUtc)
            .Take(50)
            .ToList();

        var delivered = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var notification in due)
        {
            var now = DateTimeOffset.UtcNow;
            var affected = await db.NotificationOutbox
                .Where(x => x.Id == notification.Id && x.Status == NotificationStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.Status, NotificationStatus.Delivered)
                    .SetProperty(x => x.DeliveredAtUtc, now), cancellationToken);
            if (affected == 0) continue;
            delivered++;
            db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
            {
                NotificationOutboxId = notification.Id,
                AttemptNumber = notification.AttemptCount + 1,
                Result = "SimulatedDelivered",
                Details = $"Prototype delivery through {notification.Channel}; no external message was sent."
            });
            logger.LogInformation("Simulated {Channel} delivery for notification {NotificationId}", notification.Channel, notification.Id);
        }

        if (delivered > 0) await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }
}

public sealed class NotificationDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatcherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                await dispatcher.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The simulated notification dispatcher failed; it will retry on the next interval.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
