using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task StageActivation_QueuesVersionedMultiChannelAlerts_AndDispatcherRecordsDelivery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await DemoDataSeeder.SeedAsync(db);
            var requester = await db.Users.SingleAsync(x => x.Id == DemoDataSeeder.EmployeeId);
            var documentType = await db.DocumentTypes.SingleAsync(x => x.Id == DemoDataSeeder.PurchaseDocumentTypeId);
            var amount = await db.DocumentFields.SingleAsync(x => x.DocumentTypeId == documentType.Id && x.Key == "amount");
            var request = new ApprovalRequest { RequestNumber = "PUR-ALERT-0001", DocumentTypeId = documentType.Id, DocumentType = documentType, RequesterId = requester.Id, Requester = requester, ConfirmedManagerId = DemoDataSeeder.ManagerId, Title = "Alert test", Department = requester.Department };
            request.FieldValues.Add(new RequestFieldValue { Request = request, RevisionNumber = 1, FieldDefinitionId = amount.Id, FieldKey = "amount", Label = "Amount", Value = "500" });
            request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1 });
            db.Requests.Add(request); await db.SaveChangesAsync();

            var workflow = new WorkflowService(db, new RoutingService(db), new OutboxNotificationService(db));
            await workflow.StartAsync(request, requester);

            var queued = await db.NotificationOutbox.Where(x => x.RequestId == request.Id).ToListAsync();
            Assert.Equal(9, queued.Count);
            Assert.Equal(3, queued.Count(x => x.EventType == AlertEventType.Assignment));
            Assert.Equal(3, queued.Count(x => x.EventType == AlertEventType.Reminder));
            Assert.Equal(3, queued.Count(x => x.EventType == AlertEventType.Escalation));
            Assert.Contains(queued, x => x.Channel == NotificationChannel.InApp);
            Assert.Contains(queued, x => x.Channel == NotificationChannel.Email);
            Assert.Contains(queued, x => x.Channel == NotificationChannel.Teams);
        }

        var factory = new PooledDbContextFactory<AppDbContext>(options);
        var dispatcher = new SimulatedNotificationDispatcher(factory, NullLogger<SimulatedNotificationDispatcher>.Instance);
        var delivered = await dispatcher.DispatchDueAsync();
        Assert.Equal(3, delivered);

        await using var verification = new AppDbContext(options);
        Assert.Equal(3, await verification.NotificationOutbox.CountAsync(x => x.Status == NotificationStatus.Delivered));
        Assert.Equal(3, await verification.NotificationDeliveryAttempts.CountAsync());
    }
}
