using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class ConditionEvaluatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly ConditionEvaluator evaluator = new(new FixedTimeProvider(Today));

    [Fact]
    public void NestedAndOrGroups_ReturnRecursiveExplanation()
    {
        var stage = Stage(ConditionCombinator.Or);
        var root = stage.ConditionGroups.Single();
        var first = Child(stage, root, 1, ConditionCombinator.And, "first");
        Rule(first, 1, "amount", ComparisonOperator.GreaterThan, "1000");
        Rule(first, 2, "category", ComparisonOperator.Equals, "Equipment");
        var second = Child(stage, root, 2, ConditionCombinator.And, "second");
        Rule(second, 1, "department", ComparisonOperator.Equals, "Executive");
        Rule(second, 2, "urgent", ComparisonOperator.Equals, "true");

        var result = evaluator.Evaluate(stage, Context(("amount", "4250"), ("category", "Equipment"), ("urgent", "false")), Fields());

        Assert.True(result.IsMatch);
        Assert.Equal(2, result.GroupResult!.Children.Count);
        Assert.True(result.GroupResult.Children[0].IsMatch);
        Assert.False(result.GroupResult.Children[1].IsMatch);
        Assert.Contains("OR group matched", result.Explanation);
    }

    [Fact]
    public void NestedOrGroup_FailsWhenEveryChildFails()
    {
        var stage = Stage(ConditionCombinator.And);
        var root = stage.ConditionGroups.Single();
        Rule(root, 1, "amount", ComparisonOperator.GreaterThan, "1000");
        var departments = Child(stage, root, 2, ConditionCombinator.Or, "departments");
        Rule(departments, 1, "department", ComparisonOperator.Equals, "Operations");
        Rule(departments, 2, "department", ComparisonOperator.Equals, "Executive");

        var result = evaluator.Evaluate(stage, Context([("amount", "4250")], department: "Clinical"), Fields());

        Assert.False(result.IsMatch);
        var nested = Assert.IsType<ConditionGroupEvaluationResult>(result.GroupResult!.Children[1]);
        Assert.False(nested.IsMatch);
        Assert.All(nested.Children, child => Assert.False(child.IsMatch));
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals, "Policy ABC", "policy abc", true)]
    [InlineData(ComparisonOperator.NotEquals, "Policy ABC", "different", true)]
    [InlineData(ComparisonOperator.Contains, "Clinical Policy", "POLICY", true)]
    [InlineData(ComparisonOperator.NotContains, "Clinical Policy", "draft", true)]
    [InlineData(ComparisonOperator.StartsWith, "Clinical Policy", "clinical", true)]
    [InlineData(ComparisonOperator.EndsWith, "Clinical Policy", "POLICY", true)]
    public void TextComparisons_AreCaseInsensitive(ComparisonOperator comparisonOperator, string actual, string expected, bool match)
    {
        var stage = Stage();
        Rule(stage.ConditionGroups.Single(), 1, "title", comparisonOperator, expected);

        var result = evaluator.Evaluate(stage, Context(title: actual), Fields());

        Assert.Equal(match, result.IsMatch);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals, "1000", "1000", true)]
    [InlineData(ComparisonOperator.NotEquals, "1000", "999", true)]
    [InlineData(ComparisonOperator.GreaterThan, "1000.01", "1000", true)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, "1000", "1000", true)]
    [InlineData(ComparisonOperator.LessThan, "999.99", "1000", true)]
    [InlineData(ComparisonOperator.LessThanOrEqual, "1000", "1000", true)]
    public void CurrencyComparisons_AreNumeric(ComparisonOperator comparisonOperator, string actual, string expected, bool match)
    {
        var stage = Stage();
        Rule(stage.ConditionGroups.Single(), 1, "amount", comparisonOperator, expected);
        Assert.Equal(match, evaluator.Evaluate(stage, Context(("amount", actual)), Fields()).IsMatch);
    }

    [Fact]
    public void Between_IsInclusiveForCurrencyAndDates()
    {
        var amountStage = Stage();
        Rule(amountStage.ConditionGroups.Single(), 1, "amount", ComparisonOperator.Between, "1000", "2000");
        var dateStage = Stage();
        Rule(dateStage.ConditionGroups.Single(), 1, "effective", ComparisonOperator.Between, "2026-08-01", "2026-08-19");

        Assert.True(evaluator.Evaluate(amountStage, Context(("amount", "1000")), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(amountStage, Context(("amount", "2000")), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(dateStage, Context(("effective", "2026-08-19")), Fields()).IsMatch);
    }

    [Theory]
    [InlineData(ComparisonOperator.Before, "2026-08-18", "2026-08-19", true)]
    [InlineData(ComparisonOperator.After, "2026-08-20", "2026-08-19", true)]
    [InlineData(ComparisonOperator.Equals, "2026-08-19", "2026-08-19", true)]
    [InlineData(ComparisonOperator.NotEquals, "2026-08-20", "2026-08-19", true)]
    public void DateComparisons_UseDateOnlyValues(ComparisonOperator comparisonOperator, string actual, string expected, bool match)
    {
        var stage = Stage();
        Rule(stage.ConditionGroups.Single(), 1, "effective", comparisonOperator, expected);
        Assert.Equal(match, evaluator.Evaluate(stage, Context(("effective", actual)), Fields()).IsMatch);
    }

    [Fact]
    public void RelativeDateOperators_UseInclusiveUtcCalendarWindow()
    {
        var last = Stage();
        Rule(last.ConditionGroups.Single(), 1, "effective", ComparisonOperator.InLastDays, "7");
        var next = Stage();
        Rule(next.ConditionGroups.Single(), 1, "effective", ComparisonOperator.InNextDays, "7");

        Assert.True(evaluator.Evaluate(last, Context(("effective", "2026-08-12")), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(next, Context(("effective", "2026-08-26")), Fields()).IsMatch);
        Assert.False(evaluator.Evaluate(next, Context(("effective", "2026-08-27")), Fields()).IsMatch);
    }

    [Fact]
    public void InAndNotIn_UseRelationalOperands()
    {
        var included = Stage();
        Rule(included.ConditionGroups.Single(), 1, "category", ComparisonOperator.In, "Equipment", "Subscription");
        var excluded = Stage();
        Rule(excluded.ConditionGroups.Single(), 1, "category", ComparisonOperator.NotIn, "Equipment", "Subscription");

        Assert.True(evaluator.Evaluate(included, Context(("category", "subscription")), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(excluded, Context(("category", "Travel")), Fields()).IsMatch);
    }

    [Fact]
    public void EmptyBooleanAndUserComparisons_AreTyped()
    {
        var empty = Stage();
        Rule(empty.ConditionGroups.Single(), 1, "title", ComparisonOperator.IsEmpty);
        var notEmpty = Stage();
        Rule(notEmpty.ConditionGroups.Single(), 1, "title", ComparisonOperator.IsNotEmpty);
        var boolean = Stage();
        Rule(boolean.ConditionGroups.Single(), 1, "urgent", ComparisonOperator.Equals, "true");
        var userId = Guid.NewGuid();
        var user = Stage();
        Rule(user.ConditionGroups.Single(), 1, "owner", ComparisonOperator.Equals, userId.ToString());

        Assert.True(evaluator.Evaluate(empty, Context(title: "  "), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(notEmpty, Context(title: "Request"), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(boolean, Context(("urgent", "true")), Fields()).IsMatch);
        Assert.True(evaluator.Evaluate(user, Context(("owner", userId.ToString("D").ToUpperInvariant())), Fields()).IsMatch);
    }

    [Fact]
    public void InvalidOperandAndMissingField_FailWithExplanation()
    {
        var invalid = Stage();
        Rule(invalid.ConditionGroups.Single(), 1, "amount", ComparisonOperator.GreaterThan, "not-a-number");
        var missing = Stage();
        Rule(missing.ConditionGroups.Single(), 1, "deleted", ComparisonOperator.Equals, "value");

        var invalidResult = evaluator.Evaluate(invalid, Context(("amount", "10")), Fields());
        var missingResult = evaluator.Evaluate(missing, Context(("deleted", "value")), Fields());

        Assert.False(invalidResult.IsMatch);
        Assert.Contains("invalid", Assert.IsType<ConditionRuleEvaluationResult>(invalidResult.GroupResult!.Children.Single()).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.False(missingResult.IsMatch);
        Assert.Contains("does not exist", Assert.IsType<ConditionRuleEvaluationResult>(missingResult.GroupResult!.Children.Single()).Explanation);
    }

    [Fact]
    public void ExcessiveNesting_FailsClosed()
    {
        var stage = Stage();
        var parent = stage.ConditionGroups.Single();
        for (var depth = 2; depth <= 6; depth++) parent = Child(stage, parent, 1, ConditionCombinator.And, $"depth-{depth}");
        Rule(parent, 1, "title", ComparisonOperator.Equals, "Request");

        var result = evaluator.Evaluate(stage, Context(title: "Request"), Fields());

        Assert.False(result.IsMatch);
        Assert.Contains("maximum nesting depth", Flatten(result.GroupResult!).Last().Explanation);
    }

    private static ApprovalRouteStage Stage(ConditionCombinator combinator = ConditionCombinator.And)
    {
        var stage = new ApprovalRouteStage { Name = "Conditional", StageKey = Guid.NewGuid().ToString("N"), IsConditional = true };
        stage.ConditionGroups.Add(new RouteConditionGroup { Stage = stage, StableGroupKey = "root", Combinator = combinator, Sequence = 1 });
        return stage;
    }

    private static RouteConditionGroup Child(ApprovalRouteStage stage, RouteConditionGroup parent, int sequence, ConditionCombinator combinator, string key)
    {
        var child = new RouteConditionGroup { Stage = stage, ParentGroup = parent, StableGroupKey = key, Sequence = sequence, Combinator = combinator };
        stage.ConditionGroups.Add(child);
        parent.ChildGroups.Add(child);
        return child;
    }

    private static RouteConditionRule Rule(RouteConditionGroup group, int sequence, string fieldKey, ComparisonOperator comparisonOperator, params string[] operands)
    {
        var rule = new RouteConditionRule { Group = group, StableRuleKey = Guid.NewGuid().ToString("N"), FieldKey = fieldKey, Operator = comparisonOperator, Sequence = sequence };
        for (var index = 0; index < operands.Length; index++) rule.Operands.Add(new RouteConditionOperand { Rule = rule, Sequence = index + 1, Value = operands[index] });
        group.Rules.Add(rule);
        return rule;
    }

    private static IReadOnlyCollection<ConditionField> Fields() =>
    [
        new("title", "Request title", DocumentFieldType.ShortText, []),
        new("department", "Department", DocumentFieldType.ShortText, []),
        new("amount", "Amount", DocumentFieldType.Currency, []),
        new("category", "Category", DocumentFieldType.Choice, ["Equipment", "Subscription", "Travel"]),
        new("effective", "Effective date", DocumentFieldType.Date, []),
        new("urgent", "Urgent", DocumentFieldType.Boolean, []),
        new("owner", "Owner", DocumentFieldType.User, [])
    ];

    private static ConditionEvaluationContext Context(params (string Key, string? Value)[] values) => Context(values, null, null);

    private static ConditionEvaluationContext Context((string Key, string? Value)[]? values = null, string? title = null, string? department = null) => new()
    {
        Title = title,
        Department = department,
        Values = (values ?? []).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static IEnumerable<ConditionGroupEvaluationResult> Flatten(ConditionGroupEvaluationResult root)
    {
        yield return root;
        foreach (var child in root.Children.OfType<ConditionGroupEvaluationResult>())
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
