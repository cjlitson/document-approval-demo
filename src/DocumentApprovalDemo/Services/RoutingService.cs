using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public interface IRoutingService
{
    Task<ApprovalRouteVersion> GetPublishedRouteAsync(Guid documentTypeId, CancellationToken cancellationToken = default);
    ConditionEvaluationResult EvaluateStage(ApprovalRouteStage stage, ApprovalRequest request);
    bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request);
}

public sealed class RoutingService : IRoutingService
{
    private readonly AppDbContext db;
    private readonly IConditionEvaluator evaluator;

    public RoutingService(AppDbContext db, IConditionEvaluator? evaluator = null)
    {
        this.db = db;
        this.evaluator = evaluator ?? new ConditionEvaluator(TimeProvider.System);
    }

    public async Task<ApprovalRouteVersion> GetPublishedRouteAsync(Guid documentTypeId, CancellationToken cancellationToken = default)
    {
        return await db.RouteVersions
            .Include(x => x.Route).ThenInclude(x => x.DocumentType).ThenInclude(x => x.Fields)
            .Include(x => x.Stages).ThenInclude(x => x.ConditionGroups).ThenInclude(x => x.Rules).ThenInclude(x => x.Operands)
            .Include(x => x.Stages).ThenInclude(x => x.AlertPolicies)
            .Include(x => x.Stages).ThenInclude(x => x.NamedApprover)
            .SingleAsync(x => x.Route.DocumentTypeId == documentTypeId && x.Status == RouteVersionStatus.Published, cancellationToken);
    }

    public ConditionEvaluationResult EvaluateStage(ApprovalRouteStage stage, ApprovalRequest request)
    {
        var configuredFields = stage.RouteVersion?.Route?.DocumentType?.Fields ?? [];
        var fields = ConditionField.Build(configuredFields).ToList();
        foreach (var value in request.FieldValues.Where(x => fields.All(field =>
                     !string.Equals(field.Key, x.FieldKey, StringComparison.OrdinalIgnoreCase))))
        {
            fields.Add(new ConditionField(value.FieldKey, value.Label, value.FieldType, []));
        }
        return evaluator.Evaluate(stage, ConditionEvaluationContext.FromRequest(request), fields);
    }

    public bool ShouldIncludeStage(ApprovalRouteStage stage, ApprovalRequest request) =>
        EvaluateStage(stage, request).IsMatch;
}
