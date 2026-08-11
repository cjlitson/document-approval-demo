using System.Globalization;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface IRoutingService
{
    Task<ApprovalRouteVersion> GetPublishedPurchaseRouteAsync(CancellationToken cancellationToken = default);
    bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request);
}

public sealed class RoutingService(AppDbContext db) : IRoutingService
{
    public async Task<ApprovalRouteVersion> GetPublishedPurchaseRouteAsync(CancellationToken cancellationToken = default)
    {
        return await db.RouteVersions
            .Include(x => x.Route)
            .Include(x => x.Stages).ThenInclude(x => x.Rules)
            .Include(x => x.Stages).ThenInclude(x => x.NamedApprover)
            .SingleAsync(x => x.Route.RequestType == "Purchase Request" && x.Status == RouteVersionStatus.Published, cancellationToken);
    }

    public bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request)
    {
        if (!stage.IsConditional) return true;
        if (stage.Rules.Count == 0) return false;
        return stage.Rules.All(rule => Evaluate(rule, request));
    }

    private static bool Evaluate(RouteRule rule, ApprovalRequest request)
    {
        var actual = rule.Field switch
        {
            RuleField.Amount => request.Amount.ToString(CultureInfo.InvariantCulture),
            RuleField.Subcategory => request.Subcategory,
            RuleField.Department => request.Department,
            _ => ""
        };

        if (rule.Field == RuleField.Amount)
        {
            if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualAmount) ||
                !decimal.TryParse(rule.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var configuredAmount))
                return false;

            return rule.Operator switch
            {
                ComparisonOperator.GreaterThan => actualAmount > configuredAmount,
                ComparisonOperator.GreaterThanOrEqual => actualAmount >= configuredAmount,
                ComparisonOperator.Equal => actualAmount == configuredAmount,
                ComparisonOperator.LessThan => actualAmount < configuredAmount,
                ComparisonOperator.LessThanOrEqual => actualAmount <= configuredAmount,
                _ => false
            };
        }

        return rule.Operator switch
        {
            ComparisonOperator.Equal => string.Equals(actual, rule.Value, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.Contains => actual.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

