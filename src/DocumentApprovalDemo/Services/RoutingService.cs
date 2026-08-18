using System.Globalization;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface IRoutingService
{
    Task<ApprovalRouteVersion> GetPublishedRouteAsync(Guid documentTypeId, CancellationToken cancellationToken = default);
    bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request);
}

public sealed class RoutingService(AppDbContext db) : IRoutingService
{
    public async Task<ApprovalRouteVersion> GetPublishedRouteAsync(Guid documentTypeId, CancellationToken cancellationToken = default)
    {
        return await db.RouteVersions
            .Include(x => x.Route).ThenInclude(x => x.DocumentType).ThenInclude(x => x.Fields)
            .Include(x => x.Stages).ThenInclude(x => x.Rules)
            .Include(x => x.Stages).ThenInclude(x => x.AlertPolicies)
            .Include(x => x.Stages).ThenInclude(x => x.NamedApprover)
            .SingleAsync(x => x.Route.DocumentTypeId == documentTypeId && x.Status == RouteVersionStatus.Published, cancellationToken);
    }

    public bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request)
    {
        if (!stage.IsConditional) return true;
        if (stage.Rules.Count == 0) return false;
        return stage.Rules.All(rule => Evaluate(rule, request));
    }

    private static bool Evaluate(RouteRule rule, ApprovalRequest request)
    {
        var actual = rule.FieldKey.ToLowerInvariant() switch
        {
            "title" => request.Title,
            "department" => request.Department,
            _ => request.GetFieldValue(rule.FieldKey) ?? ""
        };

        if (decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber) &&
            decimal.TryParse(rule.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var configuredNumber))
        {
            return rule.Operator switch
            {
                ComparisonOperator.GreaterThan => actualNumber > configuredNumber,
                ComparisonOperator.GreaterThanOrEqual => actualNumber >= configuredNumber,
                ComparisonOperator.Equal => actualNumber == configuredNumber,
                ComparisonOperator.NotEqual => actualNumber != configuredNumber,
                ComparisonOperator.LessThan => actualNumber < configuredNumber,
                ComparisonOperator.LessThanOrEqual => actualNumber <= configuredNumber,
                _ => false
            };
        }

        if (DateOnly.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.None, out var actualDate) &&
            DateOnly.TryParse(rule.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var configuredDate))
        {
            var comparison = actualDate.CompareTo(configuredDate);
            return rule.Operator switch
            {
                ComparisonOperator.GreaterThan => comparison > 0,
                ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
                ComparisonOperator.Equal => comparison == 0,
                ComparisonOperator.NotEqual => comparison != 0,
                ComparisonOperator.LessThan => comparison < 0,
                ComparisonOperator.LessThanOrEqual => comparison <= 0,
                _ => false
            };
        }

        return rule.Operator switch
        {
            ComparisonOperator.Equal => string.Equals(actual, rule.Value, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.NotEqual => !string.Equals(actual, rule.Value, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.Contains => actual.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
