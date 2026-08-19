using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class RouteConfigurationServiceTests
{
    [Fact]
    public void Clone_PreservesStableKeysAndSemanticsButReplacesIds()
    {
        var source = ValidRoute();
        var sourceStage = source.Stages.Single();
        var sourceGroupIds = sourceStage.ConditionGroups.Select(x => x.Id).ToHashSet();
        var sourceRuleIds = sourceStage.ConditionGroups.SelectMany(x => x.Rules).Select(x => x.Id).ToHashSet();

        var clone = new RouteVersionCloningService().CloneAsDraft(source);
        var clonedStage = clone.Stages.Single();

        Assert.Equal(source.VersionNumber + 1, clone.VersionNumber);
        Assert.Equal(RouteVersionStatus.Draft, clone.Status);
        Assert.Equal(sourceStage.StageKey, clonedStage.StageKey);
        Assert.Equal(sourceStage.ConditionGroups.Select(x => x.StableGroupKey).Order(), clonedStage.ConditionGroups.Select(x => x.StableGroupKey).Order());
        Assert.Equal(sourceStage.ConditionGroups.SelectMany(x => x.Rules).Select(x => x.StableRuleKey).Order(), clonedStage.ConditionGroups.SelectMany(x => x.Rules).Select(x => x.StableRuleKey).Order());
        Assert.DoesNotContain(clonedStage.ConditionGroups, x => sourceGroupIds.Contains(x.Id));
        Assert.DoesNotContain(clonedStage.ConditionGroups.SelectMany(x => x.Rules), x => sourceRuleIds.Contains(x.Id));
        Assert.Equal(
            ConditionFormatter.StageSummary(sourceStage, ConditionField.Build(Fields())),
            ConditionFormatter.StageSummary(clonedStage, ConditionField.Build(Fields())));
        Assert.Equal(sourceStage.AlertPolicies.Count, clonedStage.AlertPolicies.Count);
    }

    [Fact]
    public void Validation_DetectsNoStagesAndBrokenLifecycleReference()
    {
        var route = new ApprovalRouteVersion { Name = "Empty" };
        var lifecycle = new[] { new LifecycleNotificationRule { IsEnabled = true, EventType = LifecycleNotificationEvent.StageStarted, StageKey = "deleted-stage" } };

        var result = Validate(route, lifecycle);

        Assert.Contains(result.Errors, x => x.Code == "route.stages.empty");
        Assert.Contains(result.Errors, x => x.Code == "lifecycle.stage-key");
    }

    [Fact]
    public void Validation_DetectsMissingAndInactiveNamedApprovers()
    {
        var missing = ValidRoute();
        missing.Stages.Single().NamedApproverId = null;
        var inactive = ValidRoute();
        inactive.Stages.Single().NamedApproverId = InactiveUserId;

        Assert.Contains(Validate(missing).Errors, x => x.Code == "assignment.named.missing");
        Assert.Contains(Validate(inactive).Errors, x => x.Code == "assignment.named.inactive");
    }

    [Fact]
    public void Validation_DetectsInvalidUserFieldAndFieldKey()
    {
        var route = ValidRoute();
        var stage = route.Stages.Single();
        stage.AssignmentStrategy = AssignmentStrategy.UserField;
        stage.AssigneeFieldKey = "amount";
        stage.ConditionGroups.Single().Rules.Single().FieldKey = "deleted-field";

        var result = Validate(route);

        Assert.Contains(result.Errors, x => x.Code == "assignment.user-field.type");
        Assert.Contains(result.Errors, x => x.Code == "condition.field");
    }

    [Fact]
    public void Validation_DetectsOperatorOperandAndValueProblems()
    {
        var route = ValidRoute();
        var rule = route.Stages.Single().ConditionGroups.Single().Rules.Single();
        rule.Operator = ComparisonOperator.Contains;
        rule.Operands.Single().Value = "not-a-number";

        var invalidOperator = Validate(route);
        Assert.Contains(invalidOperator.Errors, x => x.Code == "condition.operator");

        rule.Operator = ComparisonOperator.Between;
        var wrongCount = Validate(route);
        Assert.Contains(wrongCount.Errors, x => x.Code == "condition.operands");

        rule.Operands.Add(new RouteConditionOperand { Rule = rule, Sequence = 2, Value = "also-not-a-number" });
        var malformed = Validate(route);
        Assert.Contains(malformed.Errors, x => x.Code == "condition.value");
    }

    [Fact]
    public void Validation_DetectsEmptyGroupAndExcessiveNesting()
    {
        var empty = ValidRoute();
        empty.Stages.Single().ConditionGroups.Single().Rules.Clear();
        Assert.Contains(Validate(empty).Errors, x => x.Code == "condition.group.empty");

        var deep = ValidRoute();
        var stage = deep.Stages.Single();
        var parent = stage.ConditionGroups.Single();
        for (var depth = 2; depth <= 6; depth++)
        {
            var child = new RouteConditionGroup { Stage = stage, ParentGroup = parent, StableGroupKey = $"depth-{depth}", Sequence = 1 };
            stage.ConditionGroups.Add(child);
            parent.ChildGroups.Add(child);
            parent = child;
        }
        parent.Rules.Add(Rule(parent, "title", ComparisonOperator.Equals, "Request"));

        Assert.Contains(Validate(deep).Errors, x => x.Code == "condition.depth");
    }

    [Fact]
    public void Validation_DetectsAlertProblems()
    {
        var route = ValidRoute();
        var reminder = route.Stages.Single().AlertPolicies.Single(x => x.EventType == AlertEventType.Reminder);
        reminder.DelayHours = 0;
        reminder.InAppEnabled = false;
        reminder.EmailEnabled = false;
        reminder.TeamsEnabled = false;

        var result = Validate(route);

        Assert.Contains(result.Errors, x => x.Code == "alert.channel");
        Assert.Contains(result.Errors, x => x.Code == "alert.delay");
    }

    [Fact]
    public void Diff_DetectsAddedRemovedMovedAssignmentConditionAndAlertChanges()
    {
        var published = RouteWithThreeStages();
        var draft = new RouteVersionCloningService().CloneAsDraft(published);
        var removed = draft.Stages.Single(x => x.StageKey == "removed");
        draft.Stages.Remove(removed);
        var changed = draft.Stages.Single(x => x.StageKey == "changed");
        changed.Sequence = 1;
        changed.AssignmentStrategy = AssignmentStrategy.RequesterManager;
        changed.NamedApproverId = null;
        changed.ConditionGroups.Single().Rules.Single().Operands.Single().Value = "2000";
        changed.AlertPolicies.Single(x => x.EventType == AlertEventType.Reminder).DelayHours = 72;
        draft.Stages.Single(x => x.StageKey == "stable").Sequence = 2;
        Stage(draft, "added", 3, "Compliance Review");

        var diff = new RouteVersionDiffService().Compare(published, draft, ConditionField.Build(Fields()));

        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Added && x.StageKey == "added");
        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Removed && x.StageKey == "removed");
        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Moved && x.StageKey == "changed");
        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Changed && x.Description == "Assignment");
        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Changed && x.Description == "Condition");
        Assert.Contains(diff.Items, x => x.Kind == RouteVersionDiffKind.Changed && x.Description == "Alert policy");
    }

    [Fact]
    public void Diff_NoChangeCase_IsEmpty()
    {
        var published = ValidRoute();
        var draft = new RouteVersionCloningService().CloneAsDraft(published);

        var diff = new RouteVersionDiffService().Compare(published, draft, ConditionField.Build(Fields()));

        Assert.False(diff.HasChanges);
        Assert.Equal("No semantic changes from the current published version.", diff.Summary);
    }

    private static readonly Guid ActiveUserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid InactiveUserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static RouteValidationResult Validate(ApprovalRouteVersion route, IReadOnlyCollection<LifecycleNotificationRule>? lifecycle = null) =>
        new RouteValidationService().Validate(route, Fields(), Users(), lifecycle ?? []);

    private static ApprovalRouteVersion ValidRoute()
    {
        var route = new ApprovalRouteVersion { RouteId = Guid.NewGuid(), VersionNumber = 3, Name = "Route v3", Status = RouteVersionStatus.Published };
        var stage = Stage(route, "approval", 1, "Executive Review");
        stage.IsConditional = true;
        var root = new RouteConditionGroup { Stage = stage, StableGroupKey = "root", Sequence = 1, Combinator = ConditionCombinator.And };
        root.Rules.Add(Rule(root, "amount", ComparisonOperator.GreaterThan, "1000"));
        stage.ConditionGroups.Add(root);
        return route;
    }

    private static ApprovalRouteVersion RouteWithThreeStages()
    {
        var route = ValidRoute();
        var changed = route.Stages.Single();
        changed.StageKey = "changed";
        changed.Sequence = 2;
        Stage(route, "stable", 1, "Manager Review");
        Stage(route, "removed", 3, "Records Review");
        return route;
    }

    private static ApprovalRouteStage Stage(ApprovalRouteVersion route, string key, int sequence, string name)
    {
        var stage = new ApprovalRouteStage
        {
            RouteVersion = route,
            StageKey = key,
            Sequence = sequence,
            Name = name,
            AssignmentStrategy = AssignmentStrategy.NamedUser,
            NamedApproverId = ActiveUserId,
            SignatureRequired = true
        };
        stage.AlertPolicies.Add(Alert(stage, AlertEventType.Assignment, 0));
        stage.AlertPolicies.Add(Alert(stage, AlertEventType.Reminder, 48));
        stage.AlertPolicies.Add(Alert(stage, AlertEventType.Escalation, 120));
        stage.AlertPolicies.Add(Alert(stage, AlertEventType.Outcome, 0));
        route.Stages.Add(stage);
        return stage;
    }

    private static RouteConditionRule Rule(RouteConditionGroup group, string field, ComparisonOperator comparisonOperator, params string[] values)
    {
        var rule = new RouteConditionRule { Group = group, StableRuleKey = Guid.NewGuid().ToString("N"), FieldKey = field, Operator = comparisonOperator, Sequence = group.Rules.Count + 1 };
        for (var index = 0; index < values.Length; index++) rule.Operands.Add(new RouteConditionOperand { Rule = rule, Sequence = index + 1, Value = values[index] });
        return rule;
    }

    private static AlertPolicy Alert(ApprovalRouteStage stage, AlertEventType eventType, int delay) => new()
    {
        Stage = stage,
        EventType = eventType,
        RecipientStrategy = eventType == AlertEventType.Escalation ? AlertRecipientStrategy.ApproverManagerOrAdministrator : eventType == AlertEventType.Outcome ? AlertRecipientStrategy.Requester : AlertRecipientStrategy.StageApprover,
        DelayHours = delay,
        InAppEnabled = true,
        EmailEnabled = true,
        MaxDeliveryAttempts = 3,
        IsEnabled = true
    };

    private static IReadOnlyCollection<DocumentFieldDefinition> Fields() =>
    [
        new() { Key = "amount", Label = "Amount", FieldType = DocumentFieldType.Currency, Sequence = 1 },
        new() { Key = "owner", Label = "Owner", FieldType = DocumentFieldType.User, Sequence = 2 }
    ];

    private static IReadOnlyCollection<ApplicationUser> Users() =>
    [
        new() { Id = ActiveUserId, FullName = "Active Approver", IsActive = true },
        new() { Id = InactiveUserId, FullName = "Inactive Approver", IsActive = false }
    ];
}
