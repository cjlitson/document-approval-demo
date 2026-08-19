using System.ComponentModel.DataAnnotations;
using DocumentApprovalDemo.Domain;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocumentApprovalDemo.ViewModels;

public sealed class DocumentTypeAdministrationIndexViewModel
{
    public string? Search { get; init; }
    public string Status { get; init; } = "active";
    public IReadOnlyList<DocumentTypeAdministrationRowViewModel> Items { get; init; } = [];
}

public sealed class DocumentTypeAdministrationRowViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";
    public string Description { get; init; } = "";
    public string Administrators { get; init; } = "Not assigned";
    public string WorkflowStatus { get; init; } = "Not configured";
    public int? WorkflowVersion { get; init; }
    public int ActiveRequests { get; init; }
    public bool IsActive { get; init; }
    public bool CanDelete { get; init; }
}

public sealed class CreateDocumentTypeViewModel
{
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Key { get; set; } = "";
    [Required, StringLength(500)] public string Description { get; set; } = "";
    [Required, StringLength(8, MinimumLength = 2)] public string NumberPrefix { get; set; } = "DOC";
    public List<DocumentFieldEditViewModel> Fields { get; set; } = [];
    public List<DocumentTypeAccessEditViewModel> Access { get; set; } = [];
    public List<LifecycleNotificationRuleEditViewModel> Notifications { get; set; } = [];
    public List<SelectListItem> Users { get; set; } = [];
}

public sealed class DocumentFieldEditViewModel
{
    public Guid? Id { get; set; }
    [StringLength(80)] public string Key { get; set; } = "";
    [StringLength(150)] public string Label { get; set; } = "";
    public DocumentFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    [StringLength(1000)] public string? HelpText { get; set; }
    [StringLength(2000)] public string? OptionsCsv { get; set; }
    public int Sequence { get; set; }
    public bool IsUsed { get; set; }
}

public sealed class DocumentTypeAccessEditViewModel
{
    public Guid? Id { get; set; }
    public Guid? UserId { get; set; }
    public DocumentTypeAccessRole AccessRole { get; set; } = DocumentTypeAccessRole.Viewer;
    public bool IsActive { get; set; } = true;
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
}

public sealed class LifecycleNotificationRuleEditViewModel
{
    public Guid? Id { get; set; }
    public LifecycleNotificationEvent EventType { get; set; } = LifecycleNotificationEvent.RequestCompleted;
    public string? StageKey { get; set; }
    public LifecycleNotificationRecipient RecipientType { get; set; } = LifecycleNotificationRecipient.DocumentTypeAdministrators;
    public Guid? NamedUserId { get; set; }
    public string? UserFieldKey { get; set; }
    public bool SendInApp { get; set; } = true;
    public bool SendEmail { get; set; }
    public bool SendTeams { get; set; }
    public bool IsEnabled { get; set; } = true;
    [Range(0, 720)] public int DelayHours { get; set; }
}

public sealed class DocumentTypeConfigurationViewModel
{
    public required DocumentType DocumentType { get; init; }
    public string ActiveTab { get; init; } = "overview";
    public IReadOnlyList<DocumentFieldEditViewModel> Fields { get; init; } = [];
    public IReadOnlyList<DocumentTypeAccessEditViewModel> Access { get; init; } = [];
    public IReadOnlyList<LifecycleNotificationRuleEditViewModel> Notifications { get; init; } = [];
    public IReadOnlyList<SelectListItem> Users { get; init; } = [];
    public IReadOnlyList<WorkflowStageOptionViewModel> WorkflowStages { get; init; } = [];
    public IReadOnlyList<ManagedRequestRowViewModel> Requests { get; init; } = [];
    public ApprovalRouteVersion? PublishedVersion { get; init; }
    public ApprovalRouteVersion? DraftVersion { get; init; }
    public bool CanDelete { get; init; }
}

public sealed record WorkflowStageOptionViewModel(string StageKey, string Name);

public sealed class ManagedRequestsViewModel
{
    public Guid? DocumentTypeId { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public IReadOnlyList<SelectListItem> DocumentTypes { get; init; } = [];
    public IReadOnlyList<ManagedRequestRowViewModel> Requests { get; init; } = [];
}

public sealed class ManagedRequestRowViewModel
{
    public Guid Id { get; init; }
    public string RequestNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string DocumentTypeName { get; init; } = "";
    public string RequesterName { get; init; } = "";
    public RequestStatus Status { get; init; }
    public string CurrentStep { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public bool NeedsAttention { get; init; }
    public int AgeDays => Math.Max(0, (int)(DateTimeOffset.UtcNow - CreatedAtUtc).TotalDays);
}

public sealed record ManagedDocumentTypeSummaryViewModel(
    Guid DocumentTypeId,
    string Name,
    int Active,
    int AwaitingApproval,
    int NeedsAttention,
    int CompletedRecently);

public sealed record DocumentFieldDialogViewModel(
    Guid DocumentTypeId,
    string DialogId,
    string Title,
    DocumentFieldEditViewModel Field);

public sealed record NotificationRuleDialogViewModel(
    Guid DocumentTypeId,
    string DialogId,
    string Title,
    LifecycleNotificationRuleEditViewModel Rule,
    IReadOnlyList<SelectListItem> Users,
    IReadOnlyList<WorkflowStageOptionViewModel> Stages,
    IReadOnlyList<DocumentFieldEditViewModel> Fields);
