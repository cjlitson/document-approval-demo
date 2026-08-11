using System.ComponentModel.DataAnnotations;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocumentApprovalDemo.ViewModels;

public sealed class DashboardViewModel
{
    public int MyOpenRequests { get; init; }
    public int MyPendingApprovals { get; init; }
    public int ApprovedRequests { get; init; }
    public IReadOnlyList<ApprovalRequest> RecentRequests { get; init; } = [];
}

public sealed class RequestFormViewModel
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required] public string Subcategory { get; set; } = "One-Time Purchase";
    [Required, StringLength(120)] public string Vendor { get; set; } = "";
    [Url, StringLength(500)] public string? PurchaseLink { get; set; }
    [Range(0.01, 100000000)] public decimal Amount { get; set; }
    [Required, StringLength(100)] public string Department { get; set; } = "";
    [Required, StringLength(4000, MinimumLength = 10)] public string BusinessJustification { get; set; } = "";
    [Required] public Guid? ManagerId { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that the selected manager is correct.")]
    public bool ManagerConfirmed { get; set; }
    [Required] public List<IFormFile> SupportingDocuments { get; set; } = [];
    public List<SelectListItem> Managers { get; set; } = [];
    public string ManagerSource { get; set; } = "Entra";
}

public sealed class ReviseRequestViewModel
{
    public Guid RequestId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required] public string Subcategory { get; set; } = "";
    [Required, StringLength(120)] public string Vendor { get; set; } = "";
    [Url, StringLength(500)] public string? PurchaseLink { get; set; }
    [Range(0.01, 100000000)] public decimal Amount { get; set; }
    [Required, StringLength(4000, MinimumLength = 10)] public string BusinessJustification { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 5)] public string ChangeSummary { get; set; } = "";
    [Required] public List<IFormFile> SupportingDocuments { get; set; } = [];
}

public sealed class ApprovalDecisionViewModel
{
    public Guid ApprovalId { get; set; }
    public DecisionType Decision { get; set; }
    [Required, StringLength(100)] public string TypedSignature { get; set; } = "";
    [StringLength(2000)] public string? Comments { get; set; }
}

public sealed class RouteDraftViewModel
{
    public Guid VersionId { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [Required] public Guid? PresidentApproverId { get; set; }
    [Required] public Guid? FinanceApproverId { get; set; }
    [Required] public ComparisonOperator PresidentAmountOperator { get; set; }
    [Range(0, 100000000)] public decimal PresidentAmountThreshold { get; set; }
    public List<SelectListItem> Approvers { get; set; } = [];
}

