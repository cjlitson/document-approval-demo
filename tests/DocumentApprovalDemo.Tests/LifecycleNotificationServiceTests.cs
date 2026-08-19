using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class LifecycleNotificationServiceTests
{
    [Fact]
    public async Task RequestCompleted_NotifiesScopedAdministratorWithoutApprovalOrSignature()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.AddPurchaseRequestAsync("PUR-LIFE-0001");
        var approvalCount = await fixture.Db.ApprovalInstances.CountAsync();

        await new LifecycleNotificationService(fixture.Db).QueueAsync(LifecycleNotificationEvent.RequestCompleted, request);
        await fixture.Db.SaveChangesAsync();

        var notification = await fixture.Db.NotificationOutbox.SingleAsync(x =>
            x.RequestId == request.Id &&
            x.UserId == DemoDataSeeder.PurchasingId &&
            x.LifecycleEventType == LifecycleNotificationEvent.RequestCompleted);
        Assert.Null(notification.ApprovalInstanceId);
        Assert.Null(notification.AlertPolicyId);
        Assert.NotNull(notification.LifecycleNotificationRuleId);
        Assert.Equal(approvalCount, await fixture.Db.ApprovalInstances.CountAsync());
        Assert.Contains("ready for operational follow-up", notification.Body);
    }

    [Fact]
    public async Task DisabledRule_DoesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rule = await fixture.Db.LifecycleNotificationRules.SingleAsync(x =>
            x.DocumentTypeId == DemoDataSeeder.PurchaseDocumentTypeId &&
            x.EventType == LifecycleNotificationEvent.RequestCompleted);
        rule.IsEnabled = false;
        await fixture.Db.SaveChangesAsync();
        var request = await fixture.AddPurchaseRequestAsync("PUR-LIFE-0002");

        await new LifecycleNotificationService(fixture.Db).QueueAsync(LifecycleNotificationEvent.RequestCompleted, request);
        await fixture.Db.SaveChangesAsync();

        Assert.False(await fixture.Db.NotificationOutbox.AnyAsync(x => x.RequestId == request.Id));
    }

    [Fact]
    public async Task StageSpecificRule_MatchesStableStageKeyOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var stages = await fixture.Db.RouteStages
            .Where(x => x.RouteVersion.Route.DocumentTypeId == DemoDataSeeder.PurchaseDocumentTypeId)
            .OrderBy(x => x.Sequence).ToListAsync();
        fixture.Db.LifecycleNotificationRules.Add(new LifecycleNotificationRule
        {
            DocumentTypeId = DemoDataSeeder.PurchaseDocumentTypeId,
            EventType = LifecycleNotificationEvent.StageCompleted,
            StageKey = stages[0].StageKey,
            RecipientType = LifecycleNotificationRecipient.NamedUser,
            NamedUserId = DemoDataSeeder.PurchasingId,
            SendInApp = true
        });
        await fixture.Db.SaveChangesAsync();
        var request = await fixture.AddPurchaseRequestAsync("PUR-LIFE-0003");
        var service = new LifecycleNotificationService(fixture.Db);

        await service.QueueAsync(LifecycleNotificationEvent.StageCompleted, request, stages[1]);
        await fixture.Db.SaveChangesAsync();
        Assert.False(await fixture.Db.NotificationOutbox.AnyAsync(x => x.RequestId == request.Id));

        await service.QueueAsync(LifecycleNotificationEvent.StageCompleted, request, stages[0]);
        await fixture.Db.SaveChangesAsync();
        Assert.Single(await fixture.Db.NotificationOutbox.Where(x => x.RequestId == request.Id).ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            Connection = connection;
            Db = db;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await DemoDataSeeder.SeedAsync(db);
            return new Fixture(connection, db);
        }

        public async Task<ApprovalRequest> AddPurchaseRequestAsync(string number)
        {
            var type = await Db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PurchaseDocumentTypeId);
            var requester = await Db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
            var request = new ApprovalRequest
            {
                RequestNumber = number,
                DocumentType = type,
                DocumentTypeId = type.Id,
                Requester = requester,
                RequesterId = requester.Id,
                ConfirmedManagerId = DemoDataSeeder.ManagerId,
                Title = "Lifecycle notification test",
                Department = requester.Department,
                Status = RequestStatus.Approved,
                CurrentRevisionNumber = 1
            };
            Db.Requests.Add(request);
            await Db.SaveChangesAsync();
            return request;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
