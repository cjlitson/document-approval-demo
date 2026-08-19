using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class RouteSimulationServiceTests
{
    [Fact]
    public async Task SimulatorAndRuntime_ReturnTheSameDecisionAndFailingExplanation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var evaluator = new ConditionEvaluator(TimeProvider.System);
        var routing = new RoutingService(db, evaluator);
        var route = await routing.GetPublishedRouteAsync(DemoDataSeeder.PurchaseDocumentTypeId);
        var stage = route.Stages.Single(x => x.Name == "Executive Review");
        var amountField = route.Route.DocumentType.Fields.Single(x => x.Key == "amount");
        var request = new ApprovalRequest { DocumentType = route.Route.DocumentType, CurrentRevisionNumber = 1, Title = "Test", Department = "Operations" };
        request.FieldValues.Add(new RequestFieldValue
        {
            Request = request,
            RevisionNumber = 1,
            FieldDefinitionId = amountField.Id,
            FieldKey = amountField.Key,
            Label = amountField.Label,
            FieldType = amountField.FieldType,
            Value = "1000"
        });
        var context = ConditionEvaluationContext.FromRequest(request);
        var fields = ConditionField.Build(route.Route.DocumentType.Fields);
        var simulation = new RouteSimulationService(evaluator).Simulate(route, context, fields, await db.Users.AsNoTracking().ToListAsync());
        var simulatedStage = simulation.Single(x => x.StageKey == stage.StageKey);

        Assert.Equal(routing.ShouldIncludeStage(stage, request), simulatedStage.Included);
        Assert.False(simulatedStage.Included);
        Assert.NotNull(simulatedStage.Evaluation.GroupResult);
        var failingRule = Assert.IsType<ConditionRuleEvaluationResult>(simulatedStage.Evaluation.GroupResult!.Children.Single());
        Assert.False(failingRule.IsMatch);
        Assert.Equal("1000", failingRule.ActualValue);
        Assert.Contains("No match", failingRule.Explanation);
    }

    [Fact]
    public async Task AssigneeSimulation_ExplainsResolvedAndMissingPersonFieldValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var evaluator = new ConditionEvaluator(TimeProvider.System);
        var route = await new RoutingService(db, evaluator).GetPublishedRouteAsync(DemoDataSeeder.PolicyDocumentTypeId);
        var fields = ConditionField.Build(route.Route.DocumentType.Fields);
        var users = await db.Users.AsNoTracking().ToListAsync();
        var context = new ConditionEvaluationContext
        {
            Department = "Operations",
            RequesterManagerId = DemoDataSeeder.ManagerId,
            Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["risk_level"] = "Low" }
        };

        var results = new RouteSimulationService(evaluator).Simulate(route, context, fields, users);

        Assert.Equal("Morgan Manager", results.Single(x => x.StageName == "Owner Manager Review").Assignee);
        var records = results.Single(x => x.StageName == "Records Approval");
        Assert.False(records.AssigneeResolved);
        Assert.Contains("records_approver", records.Assignee);
    }
}
