using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
        var route = await service.GetPublishedPurchaseRouteAsync();
        var president = route.Stages.Single(x => x.Name == "President");
        var request = new ApprovalRequest { Amount = 1000m };

        Assert.False(service.ShouldIncludeStage(president, request));

        president.Rules.Single().Operator = ComparisonOperator.GreaterThanOrEqual;
        Assert.True(service.ShouldIncludeStage(president, request));
    }

    [Fact]
    public async Task PresidentIsIncludedAboveSeededThreshold_AndFinanceAlwaysApplies()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var service = new RoutingService(db);
        var route = await service.GetPublishedPurchaseRouteAsync();
        var request = new ApprovalRequest { Amount = 1000.01m };

        Assert.True(service.ShouldIncludeStage(route.Stages.Single(x => x.Name == "President"), request));
        Assert.True(service.ShouldIncludeStage(route.Stages.Single(x => x.Name == "VP Finance"), request));
    }
}

