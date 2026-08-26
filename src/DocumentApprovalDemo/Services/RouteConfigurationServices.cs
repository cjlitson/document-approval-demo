using System.Globalization;
using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public enum RouteValidationSeverity { Error, Warning, Information }

public sealed record RouteValidationIssue(
    RouteValidationSeverity Severity,
    string Code,
    string Message,
    string? StageKey = null);

public sealed class RouteValidationResult(IReadOnlyList<RouteValidationIssue> issues)
{
    public IReadOnlyList<RouteValidationIssue> Issues { get; } = issues;
    public IReadOnlyList<RouteValidationIssue> Errors => Issues.Where(x => x.Severity == RouteValidationSeverity.Error).ToList();
    public IReadOnlyList<RouteValidationIssue> Warnings => Issues.Where(x => x.Severity == RouteValidationSeverity.Warning).ToList();
    public IReadOnlyList<RouteValidationIssue> Information => Issues.Where(x => x.Severity == RouteValidationSeverity.Information).ToList();
    public bool IsReady => Errors.Count == 0;
}

public interface IRouteValidationService
{
    RouteValidationResult Validate(
        ApprovalRouteVersion routeVersion,
        IReadOnlyCollection<DocumentFieldDefinition> fields,
        IReadOnlyCollection<ApplicationUser> users,
        IReadOnlyCollection<LifecycleNotificationRule> lifecycleRules);
}

public sealed class RouteValidationService : IRouteValidationService
{
    public RouteValidationResult Validate(
        ApprovalRouteVersion routeVersion,
        IReadOnlyCollection<DocumentFieldDefinition> fields,
        IReadOnlyCollection<ApplicationUser> users,
        IReadOnlyCollection<LifecycleNotificationRule> lifecycleRules)
    {
        var issues = new List<RouteValidationIssue>();
        var stages = routeVersion.Stages.OrderBy(x => x.Sequence).ToList();
        if (string.IsNullOrWhiteSpace(routeVersion.Name))
            Error("route.name", "Enter a route version label.");
        if (stages.Count == 0)
            Error("route.stages.empty", "Add at least one approval stage.");
        if (!stages.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(1, stages.Count)))
            Error("route.sequence", "Stage sequence numbers must be contiguous and start at 1.");

        foreach (var duplicate in stages.GroupBy(x => x.StageKey, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            Error("route.stage-key.duplicate", $"Stage key '{duplicate.Key}' is used more than once.");

        var fieldLookup = ConditionField.Build(fields).ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var userLookup = users.ToDictionary(x => x.Id);
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name))
                Error("stage.name", $"Stage {stage.Sequence} needs a name.", stage.StageKey);
            ValidateAssignment(stage, fields, userLookup, issues);
            ValidateConditions(stage, fieldLookup, issues);
            ValidateAlerts(stage, issues);
            if (!stage.SignatureRequired)
                Warning("stage.signature", $"'{stage.Name}' does not require an adopted signature.", stage.StageKey);
        }

        var stageKeys = stages.Select(x => x.StageKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in lifecycleRules.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.StageKey)))
        {
            if (!stageKeys.Contains(rule.StageKey!))
                Error("lifecycle.stage-key", $"Lifecycle rule '{rule.EventType}' references a stage that is not in this route version.");
        }

        if (issues.All(x => x.Severity != RouteValidationSeverity.Error))
            issues.Add(new(RouteValidationSeverity.Information, "route.ready",
                $"{stages.Count} stage{(stages.Count == 1 ? "" : "s")} configured; all publish-blocking checks passed."));
        return new(issues);

        void Error(string code, string message, string? stageKey = null) =>
            issues.Add(new(RouteValidationSeverity.Error, code, message, stageKey));
        void Warning(string code, string message, string? stageKey = null) =>
            issues.Add(new(RouteValidationSeverity.Warning, code, message, stageKey));
    }

    private static void ValidateAssignment(
        ApprovalRouteStage stage,
        IReadOnlyCollection<DocumentFieldDefinition> fields,
        IReadOnlyDictionary<Guid, ApplicationUser> users,
        ICollection<RouteValidationIssue> issues)
    {
        if (stage.AssignmentStrategy == AssignmentStrategy.NamedUser)
        {
            if (!stage.NamedApproverId.HasValue)
                Add("assignment.named.missing", $"'{stage.Name}' needs a named approver.");
            else if (!users.TryGetValue(stage.NamedApproverId.Value, out var user))
                Add("assignment.named.unknown", $"'{stage.Name}' references a user that does not exist.");
            else if (!user.IsActive)
                Add("assignment.named.inactive", $"'{stage.Name}' references inactive approver {user.FullName}.");
        }
        else if (stage.AssignmentStrategy == AssignmentStrategy.UserField)
        {
            var field = fields.SingleOrDefault(x => string.Equals(x.Key, stage.AssigneeFieldKey, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                Add("assignment.user-field.missing", $"'{stage.Name}' needs an existing person field.");
            else if (field.FieldType != DocumentFieldType.User)
                Add("assignment.user-field.type", $"'{stage.Name}' can only assign from a User field.");
        }

        void Add(string code, string message) =>
            issues.Add(new(RouteValidationSeverity.Error, code, message, stage.StageKey));
    }

    private static void ValidateConditions(
        ApprovalRouteStage stage,
        IReadOnlyDictionary<string, ConditionField> fields,
        ICollection<RouteValidationIssue> issues)
    {
        if (!stage.IsConditional)
        {
            if (stage.ConditionGroups.Count > 0)
                issues.Add(new(RouteValidationSeverity.Warning, "condition.unused",
                    $"'{stage.Name}' is unconditional; its saved condition tree is ignored.", stage.StageKey));
            return;
        }

        var roots = stage.ConditionGroups.Where(x => x.ParentGroupId is null && x.ParentGroup is null).ToList();
        if (roots.Count != 1)
        {
            issues.Add(new(RouteValidationSeverity.Error, "condition.root",
                $"'{stage.Name}' must have exactly one root condition group.", stage.StageKey));
            return;
        }

        var groupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ruleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateGroup(roots[0], 1);

        void ValidateGroup(RouteConditionGroup group, int depth)
        {
            if (depth > ConditionEvaluator.MaximumNestingDepth)
                Add("condition.depth", $"'{stage.Name}' exceeds the maximum condition nesting depth of {ConditionEvaluator.MaximumNestingDepth}.");
            if (string.IsNullOrWhiteSpace(group.StableGroupKey) || !groupKeys.Add(group.StableGroupKey))
                Add("condition.group-key", $"'{stage.Name}' has duplicate or missing condition group keys.");

            var children = Children(stage.ConditionGroups, group).ToList();
            if (group.Rules.Count == 0 && children.Count == 0)
                Add("condition.group.empty", $"'{stage.Name}' contains an empty condition group.");

            var childSequences = group.Rules.Select(x => x.Sequence).Concat(children.Select(x => x.Sequence)).OrderBy(x => x).ToList();
            if (childSequences.Count > 0 && !childSequences.SequenceEqual(Enumerable.Range(1, childSequences.Count)))
                Add("condition.sequence", $"'{stage.Name}' has non-contiguous condition item sequence numbers.");

            foreach (var rule in group.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.StableRuleKey) || !ruleKeys.Add(rule.StableRuleKey))
                    Add("condition.rule-key", $"'{stage.Name}' has duplicate or missing condition rule keys.");
                if (!fields.TryGetValue(rule.FieldKey, out var field))
                {
                    Add("condition.field", $"'{stage.Name}' references deleted field '{rule.FieldKey}'.");
                    continue;
                }
                if (!ConditionOperatorCatalog.IsAllowed(field.FieldType, rule.Operator))
                    Add("condition.operator", $"'{stage.Name}' uses {rule.Operator} with {field.Label}, which is not allowed for {field.FieldType}.");
                var operands = rule.Operands.OrderBy(x => x.Sequence).ToList();
                var (minimum, maximum) = ConditionOperatorCatalog.OperandRange(rule.Operator);
                if (operands.Count < minimum || operands.Count > maximum)
                    Add("condition.operands", $"'{stage.Name}' has the wrong number of values for {field.Label} {ConditionOperatorCatalog.Label(rule.Operator)}.");
                if (!operands.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(1, operands.Count)))
                    Add("condition.operand-sequence", $"'{stage.Name}' has invalid operand sequence numbers.");
                if (operands.Any(x => !ConditionOperatorCatalog.IsPersistedValueValid(field.FieldType, rule.Operator, x.Value)))
                    Add("condition.value", $"'{stage.Name}' has an invalid configured value for {field.Label}.");
            }

            foreach (var child in children) ValidateGroup(child, depth + 1);
        }

        void Add(string code, string message) =>
            issues.Add(new(RouteValidationSeverity.Error, code, message, stage.StageKey));
    }

    private static void ValidateAlerts(ApprovalRouteStage stage, ICollection<RouteValidationIssue> issues)
    {
        foreach (var policy in stage.AlertPolicies.Where(x => x.IsEnabled))
        {
            if (!policy.InAppEnabled && !policy.EmailEnabled && !policy.TeamsEnabled)
                Add("alert.channel", $"Enabled {policy.EventType} alert on '{stage.Name}' needs at least one channel.");
            if (policy.EventType is AlertEventType.Reminder or AlertEventType.Escalation && policy.DelayHours <= 0)
                Add("alert.delay", $"Enabled {policy.EventType} alert on '{stage.Name}' needs a positive delay.");
            if (policy.MaxDeliveryAttempts <= 0)
                Add("alert.attempts", $"Enabled {policy.EventType} alert on '{stage.Name}' needs at least one delivery attempt.");
        }

        var reminder = stage.AlertPolicies.FirstOrDefault(x => x.EventType == AlertEventType.Reminder && x.IsEnabled);
        var escalation = stage.AlertPolicies.FirstOrDefault(x => x.EventType == AlertEventType.Escalation && x.IsEnabled);
        if (reminder is not null && escalation is not null && escalation.DelayHours <= reminder.DelayHours)
            Add("alert.order", $"Escalation for '{stage.Name}' must occur after its reminder.");

        void Add(string code, string message) =>
            issues.Add(new(RouteValidationSeverity.Error, code, message, stage.StageKey));
    }

    internal static IEnumerable<RouteConditionGroup> Children(
        IEnumerable<RouteConditionGroup> groups,
        RouteConditionGroup parent) =>
        groups.Where(x => x.ParentGroupId == parent.Id || (x.ParentGroupId is null && ReferenceEquals(x.ParentGroup, parent)));
}

public interface IRouteVersionCloningService
{
    ApprovalRouteVersion CloneAsDraft(ApprovalRouteVersion source);
    ApprovalRouteStage CloneStage(ApprovalRouteStage source, ApprovalRouteVersion target);
}

public sealed class RouteVersionCloningService : IRouteVersionCloningService
{
    public ApprovalRouteVersion CloneAsDraft(ApprovalRouteVersion source)
    {
        var draft = new ApprovalRouteVersion
        {
            RouteId = source.RouteId,
            VersionNumber = source.VersionNumber + 1,
            Name = $"{source.Name} v{source.VersionNumber + 1}",
            Status = RouteVersionStatus.Draft
        };
        foreach (var stage in source.Stages.OrderBy(x => x.Sequence))
            draft.Stages.Add(CloneStage(stage, draft));
        return draft;
    }

    public ApprovalRouteStage CloneStage(ApprovalRouteStage source, ApprovalRouteVersion target)
    {
        var stage = new ApprovalRouteStage
        {
            RouteVersion = target,
            StageKey = source.StageKey,
            Sequence = source.Sequence,
            Name = source.Name,
            AssignmentStrategy = source.AssignmentStrategy,
            NamedApproverId = source.NamedApproverId,
            AssigneeFieldKey = source.AssigneeFieldKey,
            SignatureRequired = source.SignatureRequired,
            IsConditional = source.IsConditional
        };
        foreach (var policy in source.AlertPolicies.OrderBy(x => x.EventType))
        {
            stage.AlertPolicies.Add(new AlertPolicy
            {
                Stage = stage,
                EventType = policy.EventType,
                RecipientStrategy = policy.RecipientStrategy,
                DelayHours = policy.DelayHours,
                InAppEnabled = policy.InAppEnabled,
                EmailEnabled = policy.EmailEnabled,
                TeamsEnabled = policy.TeamsEnabled,
                MaxDeliveryAttempts = policy.MaxDeliveryAttempts,
                IsEnabled = policy.IsEnabled
            });
        }

        foreach (var root in source.ConditionGroups.Where(x => x.ParentGroupId is null && x.ParentGroup is null).OrderBy(x => x.Sequence))
            CloneGroup(root, null);
        return stage;

        void CloneGroup(RouteConditionGroup sourceGroup, RouteConditionGroup? parent)
        {
            var group = new RouteConditionGroup
            {
                Stage = stage,
                ParentGroup = parent,
                StableGroupKey = sourceGroup.StableGroupKey,
                Combinator = sourceGroup.Combinator,
                Sequence = sourceGroup.Sequence
            };
            stage.ConditionGroups.Add(group);
            parent?.ChildGroups.Add(group);
            foreach (var sourceRule in sourceGroup.Rules.OrderBy(x => x.Sequence))
            {
                var rule = new RouteConditionRule
                {
                    Group = group,
                    StableRuleKey = sourceRule.StableRuleKey,
                    FieldKey = sourceRule.FieldKey,
                    Operator = sourceRule.Operator,
                    Sequence = sourceRule.Sequence
                };
                foreach (var operand in sourceRule.Operands.OrderBy(x => x.Sequence))
                    rule.Operands.Add(new RouteConditionOperand { Rule = rule, Sequence = operand.Sequence, Value = operand.Value });
                group.Rules.Add(rule);
            }
            foreach (var child in RouteValidationService.Children(source.ConditionGroups, sourceGroup).OrderBy(x => x.Sequence))
                CloneGroup(child, group);
        }
    }
}

public enum RouteVersionDiffKind { Added, Removed, Changed, Moved }

public sealed record RouteVersionDiffItem(
    RouteVersionDiffKind Kind,
    string StageKey,
    string StageName,
    string Description,
    string? Before = null,
    string? After = null);

public sealed record RouteVersionDiffResult(
    int? PreviousVersionNumber,
    IReadOnlyList<RouteVersionDiffItem> Items)
{
    public bool HasChanges => Items.Count > 0;
    public string Summary => Items.Count == 0
        ? "No semantic changes from the current published version."
        : string.Join(", ", Items.GroupBy(x => x.Kind)
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Count()} {x.Key.ToString().ToLowerInvariant()}"));
}

public interface IRouteVersionDiffService
{
    RouteVersionDiffResult Compare(
        ApprovalRouteVersion? published,
        ApprovalRouteVersion draft,
        IReadOnlyCollection<ConditionField> fields);
}

public sealed class RouteVersionDiffService : IRouteVersionDiffService
{
    public RouteVersionDiffResult Compare(
        ApprovalRouteVersion? published,
        ApprovalRouteVersion draft,
        IReadOnlyCollection<ConditionField> fields)
    {
        if (published is null)
        {
            return new(null, draft.Stages.OrderBy(x => x.Sequence)
                .Select(x => new RouteVersionDiffItem(RouteVersionDiffKind.Added, x.StageKey, x.Name, "Stage added."))
                .ToList());
        }

        var items = new List<RouteVersionDiffItem>();
        var before = published.Stages.ToDictionary(x => x.StageKey, StringComparer.OrdinalIgnoreCase);
        var after = draft.Stages.ToDictionary(x => x.StageKey, StringComparer.OrdinalIgnoreCase);
        foreach (var stage in draft.Stages.OrderBy(x => x.Sequence).Where(x => !before.ContainsKey(x.StageKey)))
            items.Add(new(RouteVersionDiffKind.Added, stage.StageKey, stage.Name, "Stage added."));
        foreach (var stage in published.Stages.OrderBy(x => x.Sequence).Where(x => !after.ContainsKey(x.StageKey)))
            items.Add(new(RouteVersionDiffKind.Removed, stage.StageKey, stage.Name, "Stage removed."));

        foreach (var current in draft.Stages.OrderBy(x => x.Sequence).Where(x => before.ContainsKey(x.StageKey)))
        {
            var prior = before[current.StageKey];
            if (prior.Sequence != current.Sequence)
                items.Add(new(RouteVersionDiffKind.Moved, current.StageKey, current.Name,
                    $"Stage {prior.Sequence} → Stage {current.Sequence}.", prior.Sequence.ToString(), current.Sequence.ToString()));
            AddChange("Name", prior.Name, current.Name);
            AddChange("Assignment", Assignment(prior), Assignment(current));
            AddChange("Signature requirement", prior.SignatureRequired ? "Required" : "Not required", current.SignatureRequired ? "Required" : "Not required");
            AddChange("Condition", Condition(prior), Condition(current));
            AddChange("Alert policy", Alerts(prior), Alerts(current));

            void AddChange(string description, string oldValue, string newValue)
            {
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    items.Add(new(RouteVersionDiffKind.Changed, current.StageKey, current.Name, description, oldValue, newValue));
            }
        }
        return new(published.VersionNumber, items);

        string Condition(ApprovalRouteStage stage) => stage.IsConditional
            ? ConditionFormatter.StageSummary(stage, fields)
            : "Always runs";
    }

    private static string Assignment(ApprovalRouteStage stage) => stage.AssignmentStrategy switch
    {
        AssignmentStrategy.RequesterManager => "Requester manager",
        AssignmentStrategy.NamedUser => $"Named user: {stage.NamedApproverId}",
        AssignmentStrategy.UserField => $"Person field: {stage.AssigneeFieldKey}",
        _ => stage.AssignmentStrategy.ToString()
    };

    private static string Alerts(ApprovalRouteStage stage) => string.Join("; ", stage.AlertPolicies
        .OrderBy(x => x.EventType)
        .Select(x => $"{x.EventType}:{x.IsEnabled}:{x.DelayHours}:{x.RecipientStrategy}:{x.InAppEnabled}:{x.EmailEnabled}:{x.TeamsEnabled}"));
}

public sealed record RouteSimulationStageResult(
    int Sequence,
    string StageKey,
    string StageName,
    bool Included,
    string AssignmentStrategy,
    string Assignee,
    bool AssigneeResolved,
    ConditionEvaluationResult Evaluation);

public interface IRouteSimulationService
{
    IReadOnlyList<RouteSimulationStageResult> Simulate(
        ApprovalRouteVersion routeVersion,
        ConditionEvaluationContext context,
        IReadOnlyCollection<ConditionField> fields,
        IReadOnlyCollection<ApplicationUser> users);
}

public sealed class RouteSimulationService(IConditionEvaluator evaluator) : IRouteSimulationService
{
    public IReadOnlyList<RouteSimulationStageResult> Simulate(
        ApprovalRouteVersion routeVersion,
        ConditionEvaluationContext context,
        IReadOnlyCollection<ConditionField> fields,
        IReadOnlyCollection<ApplicationUser> users) =>
        routeVersion.Stages.OrderBy(x => x.Sequence).Select(stage =>
        {
            var evaluation = evaluator.Evaluate(stage, context, fields);
            var (assignee, resolved) = Resolve(stage, context, users);
            return new RouteSimulationStageResult(
                stage.Sequence,
                stage.StageKey,
                stage.Name,
                evaluation.IsMatch,
                stage.AssignmentStrategy switch
                {
                    AssignmentStrategy.RequesterManager => "Requester Manager",
                    AssignmentStrategy.NamedUser => "Named Person",
                    AssignmentStrategy.UserField => "Person From Request Field",
                    _ => stage.AssignmentStrategy.ToString()
                },
                assignee,
                resolved,
                evaluation);
        }).ToList();

    private static (string Assignee, bool Resolved) Resolve(
        ApprovalRouteStage stage,
        ConditionEvaluationContext context,
        IReadOnlyCollection<ApplicationUser> users)
    {
        Guid? userId = stage.AssignmentStrategy switch
        {
            AssignmentStrategy.RequesterManager => context.RequesterManagerId,
            AssignmentStrategy.NamedUser => stage.NamedApproverId,
            AssignmentStrategy.UserField when Guid.TryParse(context.GetValue(stage.AssigneeFieldKey ?? ""), out var value) => value,
            _ => null
        };
        if (!userId.HasValue)
            return stage.AssignmentStrategy == AssignmentStrategy.UserField
                ? ($"Assignee unresolved · {stage.AssigneeFieldKey} has no person value", false)
                : ("Assignee unresolved", false);
        var user = users.SingleOrDefault(x => x.Id == userId.Value && x.IsActive);
        return user is null ? ("Assignee unresolved · selected user is inactive or missing", false) : (user.FullName, true);
    }
}
