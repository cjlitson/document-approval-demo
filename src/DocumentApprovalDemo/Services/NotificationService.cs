using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public interface INotificationService
{
    Task QueueApprovalAssignedAsync(ApplicationUser user, ApprovalRequest request, string stageName, CancellationToken cancellationToken = default);
    Task QueueRequestOutcomeAsync(ApplicationUser user, ApprovalRequest request, string outcome, CancellationToken cancellationToken = default);
}

public sealed class DemoNotificationService(AppDbContext db, ILogger<DemoNotificationService> logger) : INotificationService
{
    public Task QueueApprovalAssignedAsync(ApplicationUser user, ApprovalRequest request, string stageName, CancellationToken cancellationToken = default) =>
        QueueBothAsync(user, request, $"Approval needed: {request.RequestNumber}",
            $"{stageName} approval is ready for {request.Title} ({request.Amount:C}).", cancellationToken);

    public Task QueueRequestOutcomeAsync(ApplicationUser user, ApprovalRequest request, string outcome, CancellationToken cancellationToken = default) =>
        QueueBothAsync(user, request, $"{request.RequestNumber} {outcome}",
            $"Your request '{request.Title}' is now {outcome.ToLowerInvariant()}.", cancellationToken);

    private async Task QueueBothAsync(ApplicationUser user, ApprovalRequest request, string subject, string body, CancellationToken cancellationToken)
    {
        foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Teams })
        {
            db.NotificationLogs.Add(new NotificationLog
            {
                UserId = user.Id, RequestId = request.Id, Channel = channel,
                Subject = subject, Body = body, DeliveryStatus = "DemoQueued"
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo queued Email and Teams notification for {Email}: {Subject}", user.Email, subject);
    }
}

