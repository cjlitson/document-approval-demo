using System.ComponentModel.DataAnnotations;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocumentApprovalDemo.ViewModels;

public sealed class DashboardViewModel
{
    public int MyOpenRequests { get; init; }
    public int MyPendingApprovals { get; init; }
    public int ApprovedRequests { get; init; }
    public int UnreadAlerts { get; init; }
    public IReadOnlyList<ApprovalRequest> RecentRequests { get; init; } = [];
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
