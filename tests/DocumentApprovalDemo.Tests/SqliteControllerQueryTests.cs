using DocumentApprovalDemo.Controllers;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class SqliteControllerQueryTests
{
    [Fact]
    public async Task Dashboard_OrdersDateTimeOffsetValuesAfterSqliteMaterialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await DemoDataSeeder.SeedAsync(db);
        var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
        db.Requests.AddRange(
            CreateRequest("PR-2026-0001", "Older", requester, DateTimeOffset.UtcNow.AddDays(-1)),
            CreateRequest("PR-2026-0002", "Newer", requester, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var controller = new HomeController(db, new TestCurrentUserService(requester));
        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var model = Assert.IsType<DashboardViewModel>(result.Model);

        Assert.Equal(new[] { "Newer", "Older" }, model.RecentRequests.Select(x => x.Title));
    }

    private static ApprovalRequest CreateRequest(string number, string title, ApplicationUser requester, DateTimeOffset createdAt) => new()
    {
        RequestNumber = number,
        RequesterId = requester.Id,
        Requester = requester,
        ConfirmedManagerId = DemoDataSeeder.ManagerId,
        Title = title,
        Subcategory = "One-Time Purchase",
        Vendor = "Test Vendor",
        Department = requester.Department,
        Amount = 100m,
        BusinessJustification = "Test dashboard query compatibility.",
        CreatedAtUtc = createdAt,
        Status = RequestStatus.InApproval
    };

    private sealed class TestCurrentUserService(ApplicationUser user) : ICurrentUserService
    {
        public Guid? UserId => user.Id;
        public Task<ApplicationUser?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationUser?>(user);
    }
}
