using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class RoutingServiceTests
{
    [Fact]
    public async Task ExactlyOneThousand_FollowsConfiguredOperator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var service = new RoutingService(db);
        var route = await service.GetPublishedRouteAsync(DemoDataSeeder.PurchaseDocumentTypeId);
        var executive = route.Stages.Single(x => x.Name == "Executive Review");
        var request = RequestWithField(DemoDataSeeder.PurchaseDocumentTypeId, "amount", "1000");

        Assert.False(service.ShouldIncludeStage(executive, request));

        executive.Rules.Single().Operator = ComparisonOperator.GreaterThanOrEqual;
        Assert.True(service.ShouldIncludeStage(executive, request));
    }

    [Fact]
    public async Task DifferentDocumentTypes_EvaluateTheirOwnFieldConditions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var service = new RoutingService(db);

        var purchaseRoute = await service.GetPublishedRouteAsync(DemoDataSeeder.PurchaseDocumentTypeId);
        Assert.True(service.ShouldIncludeStage(
            purchaseRoute.Stages.Single(x => x.Name == "Executive Review"),
            RequestWithField(DemoDataSeeder.PurchaseDocumentTypeId, "amount", "1000.01")));

        var policyRoute = await service.GetPublishedRouteAsync(DemoDataSeeder.PolicyDocumentTypeId);
        var compliance = policyRoute.Stages.Single(x => x.Name == "Compliance Review");
        Assert.True(service.ShouldIncludeStage(compliance, RequestWithField(DemoDataSeeder.PolicyDocumentTypeId, "risk_level", "High")));
        Assert.False(service.ShouldIncludeStage(compliance, RequestWithField(DemoDataSeeder.PolicyDocumentTypeId, "risk_level", "Low")));
    }

    private static ApprovalRequest RequestWithField(Guid documentTypeId, string key, string value)
    {
        var request = new ApprovalRequest { DocumentTypeId = documentTypeId, CurrentRevisionNumber = 1 };
        request.FieldValues.Add(new RequestFieldValue { Request = request, RevisionNumber = 1, FieldKey = key, Value = value });
        return request;
    }
}
