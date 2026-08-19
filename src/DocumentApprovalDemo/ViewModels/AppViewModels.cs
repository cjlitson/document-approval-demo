using System.ComponentModel.DataAnnotations;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocumentApprovalDemo.ViewModels;

public sealed class DashboardViewModel
{
    public int MyPendingApprovals { get; init; }
    public int ActiveRequests { get; init; }
    public int NeedsAttention { get; init; }
    public int CompletedLast30Days { get; init; }
    public int UnreadAlerts { get; init; }
    public IReadOnlyList<DashboardApprovalQueueItemViewModel> ApprovalQueue { get; init; } = [];
    public IReadOnlyList<DashboardRequestViewModel> RecentRequests { get; init; } = [];
    public IReadOnlyList<DashboardActivityItemViewModel> RecentActivity { get; init; } = [];
    public IReadOnlyList<ManagedDocumentTypeSummaryViewModel> ManagedDocumentTypes { get; init; } = [];
}

public sealed class DashboardApprovalQueueItemViewModel
{
    public Guid ApprovalId { get; init; }
    public Guid RequestId { get; init; }
    public string RequestNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string DocumentTypeName { get; init; } = "";
    public string RequesterName { get; init; } = "";
    public string StageName { get; init; } = "";
    public DateTimeOffset? SubmittedAtUtc { get; init; }
    public DateTimeOffset? ActivatedAtUtc { get; init; }
}

public sealed class DashboardRequestViewModel
{
    public Guid Id { get; init; }
    public string RequestNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string DocumentTypeName { get; init; } = "";
    public string CurrentStage { get; init; } = "";
    public RequestStatus Status { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class DashboardActivityItemViewModel
{
    public Guid RequestId { get; init; }
    public string RequestNumber { get; init; } = "";
    public string EventType { get; init; } = "";
    public string Details { get; init; } = "";
    public string ActorName { get; init; } = "";
    public DateTimeOffset OccurredAtUtc { get; init; }

    public string Title => EventType switch
    {
        "RequestSubmitted" => "Request submitted",
        "ApprovalDecision" => "Approval decision recorded",
        "RequestApproved" => "Workflow completed",
        "RequestRevised" => "Request revised",
        _ => "Workflow activity"
    };

    public string Tone => EventType switch
    {
        "RequestApproved" => "success",
        "ApprovalDecision" => "info",
        "RequestRevised" => "warning",
        _ => "neutral"
    };
}

public sealed record MetricCardViewModel(
    string Label,
    int Value,
    string Description,
    string ActionLabel,
    string ActionUrl,
    string Icon,
    string Tone = "neutral");

public sealed record StatusBadgeViewModel(string Label, string Tone)
{
    public static StatusBadgeViewModel For(RequestStatus status) => status switch
    {
        RequestStatus.Draft => new("Draft", "neutral"),
        RequestStatus.InApproval => new("In approval", "info"),
        RequestStatus.Rejected => new("Needs revision", "danger"),
        RequestStatus.Approved => new("Approved", "success"),
        _ => new(status.ToString(), "neutral")
    };

    public static StatusBadgeViewModel For(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Queued => new("Upcoming", "neutral"),
        ApprovalStatus.Pending => new("Awaiting action", "info"),
        ApprovalStatus.Approved => new("Approved", "success"),
        ApprovalStatus.Rejected => new("Rejected", "danger"),
        ApprovalStatus.Superseded => new("Superseded", "neutral"),
        ApprovalStatus.Skipped => new("Skipped", "neutral"),
        _ => new(status.ToString(), "neutral")
    };

    public static StatusBadgeViewModel For(NotificationStatus status) => status switch
    {
        NotificationStatus.Pending => new("Pending", "warning"),
        NotificationStatus.Delivered => new("Delivered", "success"),
        NotificationStatus.Failed => new("Failed", "danger"),
        NotificationStatus.Cancelled => new("Cancelled", "neutral"),
        _ => new(status.ToString(), "neutral")
    };

    public static StatusBadgeViewModel For(RouteVersionStatus status) => status switch
    {
        RouteVersionStatus.Draft => new("Draft", "warning"),
        RouteVersionStatus.Published => new("Published", "success"),
        RouteVersionStatus.Retired => new("Retired", "neutral"),
        _ => new(status.ToString(), "neutral")
    };
}

public sealed class RequestDetailsViewModel
{
    public required ApprovalRequest Request { get; init; }
    public required RoutingHistoryViewModel RoutingHistory { get; init; }
    public required WorkflowProgressViewModel WorkflowProgress { get; init; }
    public IReadOnlyList<RequestDocumentViewModel> Documents { get; init; } = [];
    public IReadOnlyList<RequestActivityViewModel> Activity { get; init; } = [];
    public bool PackageAvailable { get; init; }
}

public sealed class WorkflowProgressViewModel
{
    public IReadOnlyList<WorkflowProgressItemViewModel> Items { get; init; } = [];

    public static WorkflowProgressViewModel Create(ApprovalRequest request)
    {
        var approvals = request.Approvals
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
            .ToDictionary(x => x.RouteStageId);
        var items = new List<WorkflowProgressItemViewModel>
        {
            new()
            {
                Title = "Submitted",
                Description = request.SubmittedAtUtc.HasValue ? $"Submitted by {request.Requester.FullName}" : "Request draft",
                State = request.SubmittedAtUtc.HasValue ? "completed" : "current",
                TimestampUtc = request.SubmittedAtUtc
            }
        };

        var stages = request.RouteVersion?.Stages.OrderBy(x => x.Sequence).ToList() ?? [];
        if (stages.Count == 0)
        {
            stages = request.Approvals.Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
                .OrderBy(x => x.Sequence)
                .Select(x => new ApprovalRouteStage { Id = x.RouteStageId, Sequence = x.Sequence, Name = x.StageName })
                .ToList();
        }

        foreach (var stage in stages)
        {
            approvals.TryGetValue(stage.Id, out var approval);
            items.Add(new WorkflowProgressItemViewModel
            {
                Title = stage.Name,
                Description = approval is null
                    ? stage.IsConditional ? "Not required for the submitted values" : "Not executed"
                    : approval.Status switch
                    {
                        ApprovalStatus.Approved => $"Approved by {approval.Approver.FullName}",
                        ApprovalStatus.Rejected => $"Rejected by {approval.Approver.FullName}",
                        ApprovalStatus.Pending => $"Awaiting {approval.Approver.FullName}",
                        ApprovalStatus.Queued => $"Upcoming · assigned to {approval.Approver.FullName}",
                        ApprovalStatus.Superseded => "Superseded by a later revision",
                        ApprovalStatus.Skipped => "Not required",
                        _ => approval.Status.ToString()
                    },
                State = approval?.Status switch
                {
                    ApprovalStatus.Approved => "completed",
                    ApprovalStatus.Pending => "current",
                    ApprovalStatus.Rejected => "rejected",
                    ApprovalStatus.Skipped => "skipped",
                    ApprovalStatus.Superseded => "skipped",
                    ApprovalStatus.Queued => "future",
                    null => "skipped",
                    _ => "future"
                },
                TimestampUtc = approval?.CompletedAtUtc ?? approval?.ActivatedAtUtc
            });
        }

        items.Add(new WorkflowProgressItemViewModel
        {
            Title = "Completed",
            Description = request.Status switch
            {
                RequestStatus.Approved => "All required approvals completed",
                RequestStatus.Rejected => "Paused for requester revision",
                _ => "Completes after all required stages"
            },
            State = request.Status switch
            {
                RequestStatus.Approved => "completed",
                RequestStatus.Rejected => "rejected",
                _ => "future"
            },
            TimestampUtc = request.CompletedAtUtc
        });
        return new WorkflowProgressViewModel { Items = items };
    }
}

public sealed class WorkflowProgressItemViewModel
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string State { get; init; } = "future";
    public DateTimeOffset? TimestampUtc { get; init; }
}

public sealed class RequestDocumentViewModel
{
    public Guid? AttachmentId { get; init; }
    public string Name { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public int? Revision { get; init; }
    public long? SizeBytes { get; init; }
    public bool IsApprovalRecord { get; init; }
    public bool CanPreview { get; init; }
    public string? PreviewUnavailableReason { get; init; }
    public string PreviewUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}

public sealed class RequestActivityViewModel
{
    public string EventType { get; init; } = "";
    public string Details { get; init; } = "";
    public string ActorName { get; init; } = "";
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class RoutingHistoryViewModel
{
    public int RevisionNumber { get; init; }
    public int? RouteVersionNumber { get; init; }
    public IReadOnlyList<RoutingHistoryItemViewModel> Items { get; init; } = [];

    public static RoutingHistoryViewModel Create(ApprovalRequest request)
    {
        var items = new List<RoutingHistoryItemViewModel>();
        items.Add(new RoutingHistoryItemViewModel
        {
            Title = request.SubmittedAtUtc.HasValue ? "Request submitted" : "Request created",
            StatusLabel = request.SubmittedAtUtc.HasValue ? "Complete" : "Draft",
            State = request.SubmittedAtUtc.HasValue ? "completed" : "current",
            ActorName = request.Requester.FullName,
            Detail = $"{request.DocumentType.Name} · revision {request.CurrentRevisionNumber}",
            TimestampUtc = request.SubmittedAtUtc ?? request.CreatedAtUtc
        });

        var approvals = request.Approvals
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
            .OrderBy(x => x.Sequence)
            .ToList();

        for (var index = 0; index < approvals.Count; index++)
        {
            var approval = approvals[index];
            var next = approvals.Skip(index + 1).FirstOrDefault();
            var badge = StatusBadgeViewModel.For(approval.Status);
            items.Add(new RoutingHistoryItemViewModel
            {
                Title = approval.StageName,
                StatusLabel = badge.Label,
                State = approval.Status switch
                {
                    ApprovalStatus.Approved => "completed",
                    ApprovalStatus.Pending => "current",
                    ApprovalStatus.Rejected => "error",
                    ApprovalStatus.Queued => "future",
                    ApprovalStatus.Superseded => "future",
                    ApprovalStatus.Skipped => "future",
                    _ => "future"
                },
                ActorName = approval.Approver.FullName,
                Detail = approval.SignatureRequired ? "Adopted signature required" : "Signature optional",
                Transition = approval.Status == ApprovalStatus.Approved && next is not null
                    ? $"Routed to {next.StageName}."
                    : null,
                Signature = approval.Decision?.TypedSignature,
                Comments = approval.Decision?.Comments,
                TimestampUtc = approval.CompletedAtUtc ?? approval.ActivatedAtUtc
            });
        }

        items.Add(new RoutingHistoryItemViewModel
        {
            Title = "Workflow completed",
            StatusLabel = request.Status switch
            {
                RequestStatus.Approved => "Complete",
                RequestStatus.Rejected => "Waiting for revision",
                _ => "Pending"
            },
            State = request.Status switch
            {
                RequestStatus.Approved => "completed",
                RequestStatus.Rejected => "error",
                _ => "future"
            },
            Detail = request.Status == RequestStatus.Approved
                ? "All required approval stages are complete."
                : "Completes after every required stage is approved.",
            TimestampUtc = request.CompletedAtUtc
        });

        return new RoutingHistoryViewModel
        {
            RevisionNumber = request.CurrentRevisionNumber,
            RouteVersionNumber = request.RouteVersion?.VersionNumber,
            Items = items
        };
    }
}

public sealed class RoutingHistoryItemViewModel
{
    public string Title { get; init; } = "";
    public string StatusLabel { get; init; } = "";
    public string State { get; init; } = "future";
    public string? ActorName { get; init; }
    public string? Detail { get; init; }
    public string? Transition { get; init; }
    public string? Signature { get; init; }
    public string? Comments { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
}

public sealed class RequestFormViewModel
{
    [Required] public Guid? DocumentTypeId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(100)] public string Department { get; set; } = "";
    [Required] public Guid? ManagerId { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that the selected manager is correct.")]
    public bool ManagerConfirmed { get; set; }
    public List<IFormFile> SupportingDocuments { get; set; } = [];
    public List<DynamicFieldInputViewModel> Fields { get; set; } = [];
    public List<SelectListItem> DocumentTypes { get; set; } = [];
    public List<SelectListItem> Managers { get; set; } = [];
    public List<SelectListItem> Users { get; set; } = [];
    public List<RouteStagePreviewViewModel> RoutePreview { get; set; } = [];
    public string DocumentTypeName { get; set; } = "";
    public string DocumentTypeDescription { get; set; } = "";
    public string ManagerSource { get; set; } = "Entra";
}

public sealed class DynamicFieldInputViewModel
{
    public Guid FieldDefinitionId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public DocumentFieldType FieldType { get; set; }
    public int Sequence { get; set; }
    public bool IsRequired { get; set; }
    public string? HelpText { get; set; }
    public string? OptionsCsv { get; set; }
    public string? Value { get; set; }

    public IReadOnlyList<string> Options => (OptionsCsv ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class RouteStagePreviewViewModel
{
    public int Sequence { get; init; }
    public string Name { get; init; } = "";
    public string Assignment { get; init; } = "";
    public string? Condition { get; init; }
    public string Alerts { get; init; } = "";
}

public sealed class ReviseRequestViewModel
{
    public Guid RequestId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 5)] public string ChangeSummary { get; set; } = "";
    public List<IFormFile> SupportingDocuments { get; set; } = [];
    public List<DynamicFieldInputViewModel> Fields { get; set; } = [];
    public List<SelectListItem> Users { get; set; } = [];
    public string DocumentTypeName { get; set; } = "";
}

public sealed class ApprovalDecisionViewModel
{
    public Guid ApprovalId { get; set; }
    public DecisionType Decision { get; set; }
    [Required, StringLength(100)] public string TypedSignature { get; set; } = "";
    [StringLength(2000)] public string? Comments { get; set; }
}

public sealed class NotificationCenterViewModel
{
    public IReadOnlyList<NotificationOutbox> Notifications { get; init; } = [];
    public int PendingCount { get; init; }
    public int DeliveredCount { get; init; }
    public int CancelledCount { get; init; }
}
