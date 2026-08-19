using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Data;

public static class DemoDataSeeder
{
    public static readonly Guid EmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ManagerId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid PresidentId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid FinanceId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid AdminId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid PurchasingId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid CoordinatorId = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid PurchaseDocumentTypeId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid PolicyDocumentTypeId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.DocumentTypes.AnyAsync()) return;

        var manager = new ApplicationUser
        {
            Id = ManagerId, FullName = "Morgan Manager", Email = "morgan.manager@example.org",
            Department = "Operations", RolesCsv = $"{Roles.Requester},{Roles.Approver}",
            ManagerId = PresidentId, EntraObjectId = "demo-manager"
        };
        var president = new ApplicationUser
        {
            Id = PresidentId, FullName = "Pat President", Email = "pat.president@example.org",
            Department = "Executive", RolesCsv = $"{Roles.Requester},{Roles.Approver}", EntraObjectId = "demo-president"
        };
        var finance = new ApplicationUser
        {
            Id = FinanceId, FullName = "Finley Finance", Email = "finley.finance@example.org",
            Department = "Finance", RolesCsv = $"{Roles.Requester},{Roles.Approver}",
            ManagerId = PresidentId, EntraObjectId = "demo-finance"
        };
        var admin = new ApplicationUser
        {
            Id = AdminId, FullName = "Alex Admin", Email = "alex.admin@example.org",
            Department = "IT", RolesCsv = $"{Roles.Requester},{Roles.Approver},{Roles.SystemAdmin}",
            ManagerId = PresidentId, EntraObjectId = "demo-admin"
        };
        var employee = new ApplicationUser
        {
            Id = EmployeeId, FullName = "Avery Employee", Email = "avery.employee@example.org",
            Department = "Operations", RolesCsv = Roles.Requester, ManagerId = ManagerId, EntraObjectId = "demo-employee"
        };
        var purchasing = new ApplicationUser
        {
            Id = PurchasingId, FullName = "Taylor Purchasing", Email = "taylor.purchasing@example.org",
            Department = "Purchasing", RolesCsv = Roles.Requester, ManagerId = PresidentId, EntraObjectId = "demo-purchasing"
        };
        var coordinator = new ApplicationUser
        {
            Id = CoordinatorId, FullName = "Jordan Smith", Email = "jordan.smith@example.org",
            Department = "Purchasing", RolesCsv = Roles.Requester, ManagerId = PurchasingId, EntraObjectId = "demo-coordinator"
        };
        db.Users.AddRange(manager, president, finance, admin, employee, purchasing, coordinator);

        var purchase = new DocumentType
        {
            Id = PurchaseDocumentTypeId,
            Key = "purchase-request",
            Name = "Purchase Request",
            Description = "Amazon purchases, subscriptions, and minor operational spending.",
            NumberPrefix = "PUR"
        };
        AddField(purchase, "subcategory", "Purchase category", DocumentFieldType.Choice, 1, true,
            "Select the kind of purchase.", "One-Time Purchase,Subscription,Operational Expense");
        AddField(purchase, "vendor", "Vendor", DocumentFieldType.ShortText, 2, true);
        AddField(purchase, "amount", "Amount", DocumentFieldType.Currency, 3, true,
            "The route evaluates the configured threshold, including exactly $1,000.");
        AddField(purchase, "purchase_link", "Purchase or vendor link", DocumentFieldType.Url, 4, false);
        AddField(purchase, "business_justification", "Business justification", DocumentFieldType.LongText, 5, true,
            "Describe the need, expected outcome, and timing.");

        var purchaseRoute = new ApprovalRoute { Name = "Purchase Request Approval", DocumentType = purchase };
        purchase.Routes.Add(purchaseRoute);
        var purchaseVersion = PublishedVersion(purchaseRoute, "Purchase pilot route", 1);
        var managerStage = Stage(purchaseVersion, 1, "Manager Review", AssignmentStrategy.RequesterManager);
        var executiveStage = Stage(purchaseVersion, 2, "Executive Review", AssignmentStrategy.NamedUser, PresidentId, conditional: true);
        executiveStage.Rules.Add(new RouteRule
        {
            Stage = executiveStage, FieldKey = "amount", Operator = ComparisonOperator.GreaterThan, Value = "1000"
        });
        Stage(purchaseVersion, 3, "Financial Control Review", AssignmentStrategy.NamedUser, FinanceId);
        purchase.AccessAssignments.Add(new DocumentTypeAccess
        {
            DocumentType = purchase,
            User = purchasing,
            AccessRole = DocumentTypeAccessRole.Administrator,
            CreatedByUserId = AdminId
        });
        purchase.AccessAssignments.Add(new DocumentTypeAccess
        {
            DocumentType = purchase,
            User = coordinator,
            AccessRole = DocumentTypeAccessRole.Coordinator,
            CreatedByUserId = AdminId
        });
        purchase.LifecycleNotificationRules.Add(new LifecycleNotificationRule
        {
            DocumentType = purchase,
            EventType = LifecycleNotificationEvent.RequestCompleted,
            RecipientType = LifecycleNotificationRecipient.DocumentTypeAdministrators,
            SendInApp = true,
            IsEnabled = true,
            CreatedByUserId = AdminId,
            UpdatedByUserId = AdminId
        });

        var policy = new DocumentType
        {
            Id = PolicyDocumentTypeId,
            Key = "policy-approval",
            Name = "Policy Approval",
            Description = "Review and publish an internal policy using a different form and route.",
            NumberPrefix = "POL"
        };
        AddField(policy, "policy_summary", "Policy summary", DocumentFieldType.LongText, 1, true,
            "Summarize the purpose, audience, and material changes.");
        AddField(policy, "effective_date", "Proposed effective date", DocumentFieldType.Date, 2, true);
        AddField(policy, "risk_level", "Risk level", DocumentFieldType.Choice, 3, true,
            "High-risk policies add a compliance stage.", "Low,Medium,High");
        AddField(policy, "records_approver", "Records approver", DocumentFieldType.User, 4, true,
            "This demonstrates assignment from a person field on the submitted form.");

        var policyRoute = new ApprovalRoute { Name = "Policy Approval", DocumentType = policy };
        policy.Routes.Add(policyRoute);
        var policyVersion = PublishedVersion(policyRoute, "Policy governance route", 1);
        Stage(policyVersion, 1, "Owner Manager Review", AssignmentStrategy.RequesterManager);
        var complianceStage = Stage(policyVersion, 2, "Compliance Review", AssignmentStrategy.NamedUser, AdminId, conditional: true);
        complianceStage.Rules.Add(new RouteRule
        {
            Stage = complianceStage, FieldKey = "risk_level", Operator = ComparisonOperator.Equal, Value = "High"
        });
        Stage(policyVersion, 3, "Records Approval", AssignmentStrategy.UserField, assigneeFieldKey: "records_approver");

        db.DocumentTypes.AddRange(purchase, policy);
        await db.SaveChangesAsync();
    }

    private static void AddField(
        DocumentType documentType,
        string key,
        string label,
        DocumentFieldType fieldType,
        int sequence,
        bool required,
        string? helpText = null,
        string? optionsCsv = null) => documentType.Fields.Add(new DocumentFieldDefinition
        {
            DocumentType = documentType,
            Key = key,
            Label = label,
            FieldType = fieldType,
            Sequence = sequence,
            IsRequired = required,
            HelpText = helpText,
            OptionsCsv = optionsCsv
        });

    private static ApprovalRouteVersion PublishedVersion(ApprovalRoute route, string name, int versionNumber)
    {
        var version = new ApprovalRouteVersion
        {
            Route = route,
            VersionNumber = versionNumber,
            Name = name,
            Status = RouteVersionStatus.Published,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedById = AdminId
        };
        route.Versions.Add(version);
        return version;
    }

    private static ApprovalRouteStage Stage(
        ApprovalRouteVersion version,
        int sequence,
        string name,
        AssignmentStrategy strategy,
        Guid? namedApproverId = null,
        bool conditional = false,
        string? assigneeFieldKey = null)
    {
        var stage = new ApprovalRouteStage
        {
            RouteVersion = version,
            Sequence = sequence,
            StageKey = $"stage-{sequence:00}",
            Name = name,
            AssignmentStrategy = strategy,
            NamedApproverId = namedApproverId,
            AssigneeFieldKey = assigneeFieldKey,
            IsConditional = conditional,
            SignatureRequired = true
        };
        version.Stages.Add(stage);
        AddDefaultAlerts(stage);
        return stage;
    }

    private static void AddDefaultAlerts(ApprovalRouteStage stage)
    {
        stage.AlertPolicies.Add(Policy(stage, AlertEventType.Assignment, AlertRecipientStrategy.StageApprover, 0, true, true, true));
        stage.AlertPolicies.Add(Policy(stage, AlertEventType.Reminder, AlertRecipientStrategy.StageApprover, 48, true, true, true));
        stage.AlertPolicies.Add(Policy(stage, AlertEventType.Escalation, AlertRecipientStrategy.ApproverManagerOrAdministrator, 120, true, true, true));
        stage.AlertPolicies.Add(Policy(stage, AlertEventType.Outcome, AlertRecipientStrategy.Requester, 0, true, true, false));
    }

    private static AlertPolicy Policy(
        ApprovalRouteStage stage,
        AlertEventType eventType,
        AlertRecipientStrategy recipient,
        int delayHours,
        bool inApp,
        bool email,
        bool teams) => new()
        {
            Stage = stage,
            EventType = eventType,
            RecipientStrategy = recipient,
            DelayHours = delayHours,
            InAppEnabled = inApp,
            EmailEnabled = email,
            TeamsEnabled = teams,
            MaxDeliveryAttempts = 3,
            IsEnabled = true
        };
}
