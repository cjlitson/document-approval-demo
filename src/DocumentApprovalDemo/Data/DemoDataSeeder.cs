using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Data;

public static class DemoDataSeeder
{
    public static readonly Guid EmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ManagerId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid PresidentId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid FinanceId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid AdminId = Guid.Parse("10000000-0000-0000-0000-000000000005");

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        var manager = new ApplicationUser
        {
            Id = ManagerId, FullName = "Morgan Manager", Email = "morgan.manager@example.org",
            Department = "Operations", RolesCsv = $"{Roles.Requester},{Roles.Approver}", EntraObjectId = "demo-manager"
        };
        var president = new ApplicationUser
        {
            Id = PresidentId, FullName = "Pat President", Email = "pat.president@example.org",
            Department = "Executive", RolesCsv = $"{Roles.Requester},{Roles.Approver}", EntraObjectId = "demo-president"
        };
        var finance = new ApplicationUser
        {
            Id = FinanceId, FullName = "Finley Finance", Email = "finley.finance@example.org",
            Department = "Finance", RolesCsv = $"{Roles.Requester},{Roles.Approver}", EntraObjectId = "demo-finance"
        };
        var admin = new ApplicationUser
        {
            Id = AdminId, FullName = "Alex Admin", Email = "alex.admin@example.org",
            Department = "IT", RolesCsv = $"{Roles.Requester},{Roles.Approver},{Roles.SystemAdmin}", EntraObjectId = "demo-admin"
        };
        var employee = new ApplicationUser
        {
            Id = EmployeeId, FullName = "Avery Employee", Email = "avery.employee@example.org",
            Department = "Operations", RolesCsv = Roles.Requester, ManagerId = ManagerId, EntraObjectId = "demo-employee"
        };

        db.Users.AddRange(manager, president, finance, admin, employee);

        var route = new ApprovalRoute { Name = "Purchase Request Approval", RequestType = "Purchase Request" };
        var version = new ApprovalRouteVersion
        {
            Route = route,
            VersionNumber = 1,
            Name = "Pilot route",
            Status = RouteVersionStatus.Published,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedById = AdminId
        };
        var managerStage = new ApprovalRouteStage
        {
            RouteVersion = version, Sequence = 1, Name = "Manager", AssignmentType = "RequesterManager"
        };
        var presidentStage = new ApprovalRouteStage
        {
            RouteVersion = version, Sequence = 2, Name = "President", AssignmentType = "NamedUser",
            NamedApproverId = PresidentId, IsConditional = true
        };
        presidentStage.Rules.Add(new RouteRule
        {
            Stage = presidentStage, Field = RuleField.Amount,
            Operator = ComparisonOperator.GreaterThan, Value = "1000"
        });
        var financeStage = new ApprovalRouteStage
        {
            RouteVersion = version, Sequence = 3, Name = "VP Finance", AssignmentType = "NamedUser",
            NamedApproverId = FinanceId
        };
        version.Stages.Add(managerStage);
        version.Stages.Add(presidentStage);
        version.Stages.Add(financeStage);
        route.Versions.Add(version);
        db.Routes.Add(route);

        await db.SaveChangesAsync();
    }
}
