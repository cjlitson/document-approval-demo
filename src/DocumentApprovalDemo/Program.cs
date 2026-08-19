using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024);

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("ApprovalDatabase")
    ?? throw new InvalidOperationException("Connection string 'ApprovalDatabase' is missing.");

if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    SqliteDatabaseInitializer.EnsureParentDirectory(connectionString, builder.Environment.ContentRootPath);
}

void ConfigureDatabase(DbContextOptionsBuilder options)
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(ConfigureDatabase);
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddScoped<IRouteValidationService, RouteValidationService>();
builder.Services.AddScoped<IRouteVersionCloningService, RouteVersionCloningService>();
builder.Services.AddScoped<IRouteVersionDiffService, RouteVersionDiffService>();
builder.Services.AddScoped<IRouteSimulationService, RouteSimulationService>();
builder.Services.AddScoped<IRoutingService, RoutingService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IFilePreviewService, FilePreviewService>();
builder.Services.AddScoped<IDocumentAuthorizationService, DocumentAuthorizationService>();
builder.Services.AddScoped<IDocumentTypeAdministrationService, DocumentTypeAdministrationService>();
builder.Services.AddScoped<INotificationService, OutboxNotificationService>();
builder.Services.AddScoped<ILifecycleNotificationService, LifecycleNotificationService>();
builder.Services.AddScoped<INotificationDispatcher, SimulatedNotificationDispatcher>();
builder.Services.AddHostedService<NotificationDispatcherWorker>();
builder.Services.AddScoped<IApprovalRecordService, ApprovalRecordService>();
builder.Services.AddScoped<IDocumentPackageService, DocumentPackageService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorComponents<DocumentApprovalDemo.Components.App>()
    .AddInteractiveServerRenderMode();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DemoDataSeeder.SeedAsync(db);
}

await app.RunAsync();

public partial class Program;
