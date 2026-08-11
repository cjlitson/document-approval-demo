using System.ComponentModel.DataAnnotations;

namespace DocumentApprovalDemo.Domain;

public enum RequestStatus { Draft, InApproval, Rejected, Approved }
public enum RevisionStatus { InApproval, Rejected, Approved, Superseded }
public enum ApprovalStatus { Queued, Pending, Approved, Rejected, Superseded, Skipped }
public enum DecisionType { Approve, Reject }
public enum RouteVersionStatus { Draft, Published, Retired }
public enum RuleField { Amount, Subcategory, Department }
public enum ComparisonOperator { GreaterThan, GreaterThanOrEqual, Equal, LessThan, LessThanOrEqual, Contains }
public enum NotificationChannel { Email, Teams }

public static class Roles
{
    public const string Requester = "Requester";
    public const string Approver = "Approver";
    public const string SystemAdmin = "SystemAdmin";
}

public sealed class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(100)] public string Department { get; set; } = "";
    [MaxLength(100)] public string RolesCsv { get; set; } = global::DocumentApprovalDemo.Domain.Roles.Requester;
    public Guid? ManagerId { get; set; }
    public ApplicationUser? Manager { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(100)] public string? EntraObjectId { get; set; }

    public IEnumerable<string> Roles => RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public sealed class ApprovalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(30)] public string RequestNumber { get; set; } = "";
    public Guid RequesterId { get; set; }
    public ApplicationUser Requester { get; set; } = null!;
    public Guid ConfirmedManagerId { get; set; }
    public ApplicationUser ConfirmedManager { get; set; } = null!;
    [MaxLength(30)] public string ManagerSource { get; set; } = "Entra";
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(100)] public string Subcategory { get; set; } = "";
    [MaxLength(120)] public string Vendor { get; set; } = "";
    [MaxLength(500)] public string? PurchaseLink { get; set; }
    [MaxLength(100)] public string Department { get; set; } = "";
    public decimal Amount { get; set; }
    [MaxLength(4000)] public string BusinessJustification { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Draft;
    public int CurrentRevisionNumber { get; set; } = 1;
    public Guid? RouteVersionId { get; set; }
    public ApprovalRouteVersion? RouteVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ICollection<RequestRevision> Revisions { get; set; } = new List<RequestRevision>();
    public ICollection<RequestAttachment> Attachments { get; set; } = new List<RequestAttachment>();
    public ICollection<ApprovalInstance> Approvals { get; set; } = new List<ApprovalInstance>();
}

public sealed class RequestRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public ApprovalRequest Request { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public RevisionStatus Status { get; set; } = RevisionStatus.InApproval;
    [MaxLength(1000)] public string ChangeSummary { get; set; } = "Initial submission";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RequestAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public ApprovalRequest Request { get; set; } = null!;
    public int RevisionNumber { get; set; }
    [MaxLength(260)] public string OriginalFileName { get; set; } = "";
    [MaxLength(260)] public string StoredFileName { get; set; } = "";
    [MaxLength(150)] public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApprovalRoute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(100)] public string RequestType { get; set; } = "Purchase Request";
    public ICollection<ApprovalRouteVersion> Versions { get; set; } = new List<ApprovalRouteVersion>();
}

public sealed class ApprovalRouteVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteId { get; set; }
    public ApprovalRoute Route { get; set; } = null!;
    public int VersionNumber { get; set; }
    [MaxLength(150)] public string Name { get; set; } = "";
    public RouteVersionStatus Status { get; set; } = RouteVersionStatus.Draft;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public Guid? PublishedById { get; set; }
    public ICollection<ApprovalRouteStage> Stages { get; set; } = new List<ApprovalRouteStage>();
}

public sealed class ApprovalRouteStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteVersionId { get; set; }
    public ApprovalRouteVersion RouteVersion { get; set; } = null!;
    public int Sequence { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(30)] public string AssignmentType { get; set; } = "NamedUser";
    public Guid? NamedApproverId { get; set; }
    public ApplicationUser? NamedApprover { get; set; }
    public bool IsConditional { get; set; }
    public ICollection<RouteRule> Rules { get; set; } = new List<RouteRule>();
}

public sealed class RouteRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StageId { get; set; }
    public ApprovalRouteStage Stage { get; set; } = null!;
    public RuleField Field { get; set; }
    public ComparisonOperator Operator { get; set; }
    [MaxLength(200)] public string Value { get; set; } = "";
}

public sealed class ApprovalInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public ApprovalRequest Request { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public Guid RouteVersionId { get; set; }
    public Guid RouteStageId { get; set; }
    public int Sequence { get; set; }
    [MaxLength(100)] public string StageName { get; set; } = "";
    public Guid ApproverId { get; set; }
    public ApplicationUser Approver { get; set; } = null!;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Queued;
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ApprovalDecision? Decision { get; set; }
}

public sealed class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApprovalInstanceId { get; set; }
    public ApprovalInstance ApprovalInstance { get; set; } = null!;
    public DecisionType Decision { get; set; }
    [MaxLength(100)] public string TypedSignature { get; set; } = "";
    [MaxLength(100)] public string AuthenticatedFullName { get; set; } = "";
    [MaxLength(200)] public string AuthenticatedEmail { get; set; } = "";
    [MaxLength(100)] public string AuthenticationMethod { get; set; } = "DemoCookie";
    [MaxLength(2000)] public string? Comments { get; set; }
    public DateTimeOffset DecidedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? RequestId { get; set; }
    public NotificationChannel Channel { get; set; }
    [MaxLength(200)] public string Subject { get; set; } = "";
    [MaxLength(2000)] public string Body { get; set; } = "";
    [MaxLength(30)] public string DeliveryStatus { get; set; } = "DemoQueued";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RequestId { get; set; }
    public Guid ActorUserId { get; set; }
    [MaxLength(100)] public string EventType { get; set; } = "";
    [MaxLength(4000)] public string Details { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
