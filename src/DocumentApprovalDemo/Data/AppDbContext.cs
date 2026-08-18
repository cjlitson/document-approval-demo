using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<DocumentFieldDefinition> DocumentFields => Set<DocumentFieldDefinition>();
    public DbSet<ApprovalRequest> Requests => Set<ApprovalRequest>();
    public DbSet<RequestFieldValue> RequestFieldValues => Set<RequestFieldValue>();
    public DbSet<RequestRevision> RequestRevisions => Set<RequestRevision>();
    public DbSet<RequestAttachment> Attachments => Set<RequestAttachment>();
    public DbSet<ApprovalRoute> Routes => Set<ApprovalRoute>();
    public DbSet<ApprovalRouteVersion> RouteVersions => Set<ApprovalRouteVersion>();
    public DbSet<ApprovalRouteStage> RouteStages => Set<ApprovalRouteStage>();
    public DbSet<RouteRule> RouteRules => Set<RouteRule>();
    public DbSet<AlertPolicy> AlertPolicies => Set<AlertPolicy>();
    public DbSet<ApprovalInstance> ApprovalInstances => Set<ApprovalInstance>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>()
            .HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentType>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<DocumentFieldDefinition>().HasIndex(x => new { x.DocumentTypeId, x.Key }).IsUnique();

        modelBuilder.Entity<ApprovalRequest>().HasIndex(x => x.RequestNumber).IsUnique();
        modelBuilder.Entity<ApprovalRequest>()
            .HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApprovalRequest>()
            .HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApprovalRequest>()
            .HasOne(x => x.ConfirmedManager).WithMany().HasForeignKey(x => x.ConfirmedManagerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RequestFieldValue>().HasIndex(x => new { x.RequestId, x.RevisionNumber, x.FieldKey }).IsUnique();

        modelBuilder.Entity<ApprovalRoute>().HasIndex(x => x.DocumentTypeId).IsUnique();
        modelBuilder.Entity<ApprovalRoute>()
            .HasOne(x => x.DocumentType).WithMany(x => x.Routes).HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApprovalRouteVersion>().HasIndex(x => new { x.RouteId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<ApprovalRouteStage>()
            .HasOne(x => x.NamedApprover).WithMany().HasForeignKey(x => x.NamedApproverId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalInstance>()
            .HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApprovalInstance>()
            .HasOne(x => x.Decision).WithOne(x => x.ApprovalInstance)
            .HasForeignKey<ApprovalDecision>(x => x.ApprovalInstanceId);

        modelBuilder.Entity<NotificationOutbox>().HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<NotificationOutbox>()
            .HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationOutbox>()
            .HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
