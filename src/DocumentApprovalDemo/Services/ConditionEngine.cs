using System.Globalization;
using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public sealed record ConditionField(
    string Key,
    string Label,
    DocumentFieldType FieldType,
    IReadOnlyList<string> Options)
{
    public static IReadOnlyList<ConditionField> Build(IEnumerable<DocumentFieldDefinition> fields) =>
    [
        new("title", "Request title", DocumentFieldType.ShortText, []),
        new("department", "Department", DocumentFieldType.ShortText, []),
        .. fields.OrderBy(x => x.Sequence).Select(x => new ConditionField(x.Key, x.Label, x.FieldType, x.Options))
    ];
}

public sealed class ConditionEvaluationContext
{
    public string? Title { get; init; }
    public string? Department { get; init; }
    public Guid? RequesterManagerId { get; init; }
    public IReadOnlyDictionary<string, string?> Values { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(string fieldKey) => fieldKey.ToLowerInvariant() switch
    {
        "title" => Title,
        "department" => Department,
        _ => Values.TryGetValue(fieldKey, out var value) ? value : null
    };

    public static ConditionEvaluationContext FromRequest(ApprovalRequest request) => new()
    {
        Title = request.Title,
        Department = request.Department,
        RequesterManagerId = request.ConfirmedManagerId,
        Values = request.FieldValues
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
            .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (string?)x.Last().Value, StringComparer.OrdinalIgnoreCase)
    };
}

public abstract record ConditionNodeEvaluationResult(int Sequence, bool IsMatch);

public sealed record ConditionRuleEvaluationResult(
    int Sequence,
    bool IsMatch,
    string FieldKey,
    string FieldLabel,
    ComparisonOperator Operator,
    IReadOnlyList<string> ExpectedValues,
    string? ActualValue,
    string Explanation) : ConditionNodeEvaluationResult(Sequence, IsMatch);

public sealed record ConditionGroupEvaluationResult(
    int Sequence,
    bool IsMatch,
    string StableGroupKey,
    ConditionCombinator Combinator,
    IReadOnlyList<ConditionNodeEvaluationResult> Children,
    string Explanation) : ConditionNodeEvaluationResult(Sequence, IsMatch);

public sealed record ConditionEvaluationResult(
    bool IsMatch,
    ConditionGroupEvaluationResult? GroupResult,
    string Explanation);

public static class ConditionOperatorCatalog
{
    private static readonly ComparisonOperator[] TextOperators =
    [
        ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.Contains,
        ComparisonOperator.NotContains, ComparisonOperator.StartsWith, ComparisonOperator.EndsWith,
        ComparisonOperator.IsEmpty, ComparisonOperator.IsNotEmpty
    ];

    private static readonly ComparisonOperator[] NumberOperators =
    [
        ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.GreaterThan,
        ComparisonOperator.GreaterThanOrEqual, ComparisonOperator.LessThan,
        ComparisonOperator.LessThanOrEqual, ComparisonOperator.Between,
        ComparisonOperator.IsEmpty, ComparisonOperator.IsNotEmpty
    ];

    private static readonly ComparisonOperator[] DateOperators =
    [
        ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.Before,
        ComparisonOperator.After, ComparisonOperator.Between, ComparisonOperator.InLastDays,
        ComparisonOperator.InNextDays, ComparisonOperator.IsEmpty, ComparisonOperator.IsNotEmpty
    ];

    private static readonly ComparisonOperator[] ChoiceOperators =
    [
        ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.In,
        ComparisonOperator.NotIn, ComparisonOperator.IsEmpty, ComparisonOperator.IsNotEmpty
    ];

    private static readonly ComparisonOperator[] BooleanOperators =
    [ComparisonOperator.Equals, ComparisonOperator.NotEquals];

    private static readonly ComparisonOperator[] UserOperators =
    [ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.IsEmpty, ComparisonOperator.IsNotEmpty];

    public static IReadOnlyList<ComparisonOperator> For(DocumentFieldType fieldType) => fieldType switch
    {
        DocumentFieldType.Currency => NumberOperators,
        DocumentFieldType.Date => DateOperators,
        DocumentFieldType.Choice => ChoiceOperators,
        DocumentFieldType.Boolean => BooleanOperators,
        DocumentFieldType.User => UserOperators,
        _ => TextOperators
    };

    public static bool IsAllowed(DocumentFieldType fieldType, ComparisonOperator comparisonOperator) =>
        For(fieldType).Contains(comparisonOperator);

    public static (int Minimum, int Maximum) OperandRange(ComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        ComparisonOperator.IsEmpty or ComparisonOperator.IsNotEmpty => (0, 0),
        ComparisonOperator.Between => (2, 2),
        ComparisonOperator.In or ComparisonOperator.NotIn => (1, int.MaxValue),
        _ => (1, 1)
    };

    public static string Label(ComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        ComparisonOperator.Equals => "equals",
        ComparisonOperator.NotEquals => "does not equal",
        ComparisonOperator.Contains => "contains",
        ComparisonOperator.NotContains => "does not contain",
        ComparisonOperator.StartsWith => "starts with",
        ComparisonOperator.EndsWith => "ends with",
        ComparisonOperator.IsEmpty => "is empty",
        ComparisonOperator.IsNotEmpty => "is not empty",
        ComparisonOperator.GreaterThan => "greater than",
        ComparisonOperator.GreaterThanOrEqual => "greater than or equal",
        ComparisonOperator.LessThan => "less than",
        ComparisonOperator.LessThanOrEqual => "less than or equal",
        ComparisonOperator.Between => "between (inclusive)",
        ComparisonOperator.Before => "before",
        ComparisonOperator.After => "after",
        ComparisonOperator.InLastDays => "in the last days",
        ComparisonOperator.InNextDays => "in the next days",
        ComparisonOperator.In => "is one of",
        ComparisonOperator.NotIn => "is not one of",
        _ => comparisonOperator.ToString()
    };

    public static bool IsPersistedValueValid(DocumentFieldType fieldType, ComparisonOperator comparisonOperator, string value)
    {
        if (comparisonOperator is ComparisonOperator.IsEmpty or ComparisonOperator.IsNotEmpty) return true;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (comparisonOperator is ComparisonOperator.InLastDays or ComparisonOperator.InNextDays)
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= 0;
        return fieldType switch
        {
            DocumentFieldType.Currency => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            DocumentFieldType.Date => TryDate(value, out _),
            DocumentFieldType.Boolean => bool.TryParse(value, out _),
            DocumentFieldType.User when comparisonOperator is ComparisonOperator.Equals or ComparisonOperator.NotEquals => Guid.TryParse(value, out _),
            _ => true
        };
    }

    internal static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public interface IConditionEvaluator
{
    ConditionEvaluationResult Evaluate(
        ApprovalRouteStage stage,
        ConditionEvaluationContext context,
        IReadOnlyCollection<ConditionField> fields);
}

public sealed class ConditionEvaluator(TimeProvider timeProvider) : IConditionEvaluator
{
    public const int MaximumNestingDepth = 5;

    public ConditionEvaluationResult Evaluate(
        ApprovalRouteStage stage,
        ConditionEvaluationContext context,
        IReadOnlyCollection<ConditionField> fields)
    {
        if (!stage.IsConditional)
            return new(true, null, "Always runs.");

        var root = stage.ConditionGroups.SingleOrDefault(x => x.ParentGroupId is null && x.ParentGroup is null);
        if (root is null)
            return new(false, null, "Conditional stage has no root condition group.");

        var fieldLookup = fields.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var result = EvaluateGroup(root, stage.ConditionGroups, context, fieldLookup, 1);
        return new(result.IsMatch, result, result.Explanation);
    }

    private ConditionGroupEvaluationResult EvaluateGroup(
        RouteConditionGroup group,
        ICollection<RouteConditionGroup> allGroups,
        ConditionEvaluationContext context,
        IReadOnlyDictionary<string, ConditionField> fields,
        int depth)
    {
        if (depth > MaximumNestingDepth)
            return new(group.Sequence, false, group.StableGroupKey, group.Combinator, [],
                $"{group.Combinator.ToString().ToUpperInvariant()} group exceeds the maximum nesting depth.");

        var children = new List<ConditionNodeEvaluationResult>();
        children.AddRange(group.Rules.OrderBy(x => x.Sequence).Select(x => EvaluateRule(x, context, fields)));
        children.AddRange(allGroups.Where(x => x.ParentGroupId == group.Id ||
                                                (x.ParentGroupId is null && x.ParentGroup == group))
            .OrderBy(x => x.Sequence)
            .Select(x => EvaluateGroup(x, allGroups, context, fields, depth + 1)));
        children = children.OrderBy(x => x.Sequence).ToList();

        var isMatch = children.Count > 0 && (group.Combinator == ConditionCombinator.And
            ? children.All(x => x.IsMatch)
            : children.Any(x => x.IsMatch));
        var explanation = children.Count == 0
            ? $"{group.Combinator.ToString().ToUpperInvariant()} group is empty."
            : $"{group.Combinator.ToString().ToUpperInvariant()} group {(isMatch ? "matched" : "did not match")}.";
        return new(group.Sequence, isMatch, group.StableGroupKey, group.Combinator, children, explanation);
    }

    private ConditionRuleEvaluationResult EvaluateRule(
        RouteConditionRule rule,
        ConditionEvaluationContext context,
        IReadOnlyDictionary<string, ConditionField> fields)
    {
        var operands = rule.Operands.OrderBy(x => x.Sequence).Select(x => x.Value).ToList();
        var actual = context.GetValue(rule.FieldKey);
        if (!fields.TryGetValue(rule.FieldKey, out var field))
            return Result(false, rule, rule.FieldKey, operands, actual, "Configured field does not exist.");
        if (!ConditionOperatorCatalog.IsAllowed(field.FieldType, rule.Operator))
            return Result(false, rule, field.Label, operands, actual, "Operator is not valid for this field type.");
        var (minimum, maximum) = ConditionOperatorCatalog.OperandRange(rule.Operator);
        if (operands.Count < minimum || operands.Count > maximum)
            return Result(false, rule, field.Label, operands, actual, "Configured operand count is invalid.");
        if (operands.Any(x => !ConditionOperatorCatalog.IsPersistedValueValid(field.FieldType, rule.Operator, x)))
            return Result(false, rule, field.Label, operands, actual, "A configured value is invalid.");

        var isMatch = EvaluateTyped(field.FieldType, rule.Operator, actual, operands);
        var expected = operands.Count == 0 ? "no value" : string.Join(" and ", operands);
        var shownActual = string.IsNullOrWhiteSpace(actual) ? "empty" : actual;
        return Result(isMatch, rule, field.Label, operands, actual,
            $"{field.Label} {ConditionOperatorCatalog.Label(rule.Operator)} {expected}. Actual: {shownActual}. {(isMatch ? "Match" : "No match")}.");
    }

    private bool EvaluateTyped(
        DocumentFieldType fieldType,
        ComparisonOperator comparisonOperator,
        string? actual,
        IReadOnlyList<string> operands)
    {
        var isEmpty = string.IsNullOrWhiteSpace(actual);
        if (comparisonOperator == ComparisonOperator.IsEmpty) return isEmpty;
        if (comparisonOperator == ComparisonOperator.IsNotEmpty) return !isEmpty;
        if (isEmpty) return false;

        return fieldType switch
        {
            DocumentFieldType.Currency => EvaluateNumber(actual!, comparisonOperator, operands),
            DocumentFieldType.Date => EvaluateDate(actual!, comparisonOperator, operands),
            DocumentFieldType.Boolean => EvaluateBoolean(actual!, comparisonOperator, operands),
            DocumentFieldType.User => EvaluateUser(actual!, comparisonOperator, operands),
            DocumentFieldType.Choice => EvaluateChoice(actual!, comparisonOperator, operands),
            _ => EvaluateText(actual!, comparisonOperator, operands)
        };
    }

    private static bool EvaluateText(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands) => comparisonOperator switch
    {
        ComparisonOperator.Equals => string.Equals(actual, operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.NotEquals => !string.Equals(actual, operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.Contains => actual.Contains(operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.NotContains => !actual.Contains(operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.StartsWith => actual.StartsWith(operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.EndsWith => actual.EndsWith(operands[0], StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static bool EvaluateNumber(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands)
    {
        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return false;
        var expected = operands.Select(x => decimal.Parse(x, NumberStyles.Number, CultureInfo.InvariantCulture)).ToList();
        return comparisonOperator switch
        {
            ComparisonOperator.Equals => value == expected[0],
            ComparisonOperator.NotEquals => value != expected[0],
            ComparisonOperator.GreaterThan => value > expected[0],
            ComparisonOperator.GreaterThanOrEqual => value >= expected[0],
            ComparisonOperator.LessThan => value < expected[0],
            ComparisonOperator.LessThanOrEqual => value <= expected[0],
            ComparisonOperator.Between => value >= expected[0] && value <= expected[1],
            _ => false
        };
    }

    private bool EvaluateDate(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands)
    {
        if (!ConditionOperatorCatalog.TryDate(actual, out var value)) return false;
        if (comparisonOperator is ComparisonOperator.InLastDays or ComparisonOperator.InNextDays)
        {
            var days = int.Parse(operands[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            return comparisonOperator == ComparisonOperator.InLastDays
                ? value >= today.AddDays(-days) && value <= today
                : value >= today && value <= today.AddDays(days);
        }

        var expected = operands.Select(x =>
        {
            ConditionOperatorCatalog.TryDate(x, out var date);
            return date;
        }).ToList();
        return comparisonOperator switch
        {
            ComparisonOperator.Equals => value == expected[0],
            ComparisonOperator.NotEquals => value != expected[0],
            ComparisonOperator.Before => value < expected[0],
            ComparisonOperator.After => value > expected[0],
            ComparisonOperator.Between => value >= expected[0] && value <= expected[1],
            _ => false
        };
    }

    private static bool EvaluateBoolean(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands) =>
        bool.TryParse(actual, out var value) && bool.TryParse(operands[0], out var expected) && comparisonOperator switch
        {
            ComparisonOperator.Equals => value == expected,
            ComparisonOperator.NotEquals => value != expected,
            _ => false
        };

    private static bool EvaluateUser(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands) =>
        Guid.TryParse(actual, out var value) && Guid.TryParse(operands[0], out var expected) && comparisonOperator switch
        {
            ComparisonOperator.Equals => value == expected,
            ComparisonOperator.NotEquals => value != expected,
            _ => false
        };

    private static bool EvaluateChoice(string actual, ComparisonOperator comparisonOperator, IReadOnlyList<string> operands) => comparisonOperator switch
    {
        ComparisonOperator.Equals => string.Equals(actual, operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.NotEquals => !string.Equals(actual, operands[0], StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.In => operands.Contains(actual, StringComparer.OrdinalIgnoreCase),
        ComparisonOperator.NotIn => !operands.Contains(actual, StringComparer.OrdinalIgnoreCase),
        _ => false
    };

    private static ConditionRuleEvaluationResult Result(
        bool isMatch,
        RouteConditionRule rule,
        string label,
        IReadOnlyList<string> operands,
        string? actual,
        string explanation) =>
        new(rule.Sequence, isMatch, rule.FieldKey, label, rule.Operator, operands, actual, explanation);
}

public static class ConditionFormatter
{
    public static string StageSummary(ApprovalRouteStage stage, IReadOnlyCollection<ConditionField> fields)
    {
        if (!stage.IsConditional) return "Always runs";
        var root = stage.ConditionGroups.SingleOrDefault(x => x.ParentGroupId is null && x.ParentGroup is null);
        return root is null ? "Conditional · incomplete" : FormatGroup(root, stage.ConditionGroups, fields);
    }

    public static (int Groups, int Rules) Count(ApprovalRouteStage stage) =>
        (stage.ConditionGroups.Count, stage.ConditionGroups.Sum(x => x.Rules.Count));

    private static string FormatGroup(
        RouteConditionGroup group,
        ICollection<RouteConditionGroup> allGroups,
        IReadOnlyCollection<ConditionField> fields)
    {
        var parts = new List<(int Sequence, string Text)>();
        parts.AddRange(group.Rules.Select(rule => (rule.Sequence, FormatRule(rule, fields))));
        parts.AddRange(allGroups.Where(x => x.ParentGroupId == group.Id ||
                                           (x.ParentGroupId is null && ReferenceEquals(x.ParentGroup, group)))
            .Select(child => (child.Sequence, $"({FormatGroup(child, allGroups, fields)})")));
        return string.Join($" {group.Combinator.ToString().ToUpperInvariant()} ", parts.OrderBy(x => x.Sequence).Select(x => x.Text));
    }

    private static string FormatRule(RouteConditionRule rule, IReadOnlyCollection<ConditionField> fields)
    {
        var label = fields.FirstOrDefault(x => string.Equals(x.Key, rule.FieldKey, StringComparison.OrdinalIgnoreCase))?.Label ?? rule.FieldKey;
        var operands = rule.Operands.OrderBy(x => x.Sequence).Select(x => x.Value).ToList();
        return operands.Count == 0
            ? $"{label} {ConditionOperatorCatalog.Label(rule.Operator)}"
            : $"{label} {ConditionOperatorCatalog.Label(rule.Operator)} {string.Join(" and ", operands)}";
    }
}
