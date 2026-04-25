using FingerprintManagementSystem.ApiAdapter.Alpeta;
using FingerprintManagementSystem.ApiAdapter.Implementations;
using FingerprintManagementSystem.ApiAdapter.Persistence;
using FingerprintManagementSystem.ApiAdapter.Soap;
using FingerprintManagementSystem.Contracts;
using Microsoft.EntityFrameworkCore;
using FingerprintManagementSystem.Web.Services;
using FingerprintManagementSystem.ApiAdapter;
using FingerprintManagementSystem.Web.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<SessionGuardFilter>();
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();


var connectionString = builder.Configuration.GetConnectionString("serverDB")
                       ?? throw new Exception("Missing ConnectionStrings:serverDB");
Console.WriteLine($"[DEBUG] Using connection: {connectionString}");


builder.Services.AddDbContext<LocalAppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<RegionMappingService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddHttpClient(); // مهم لأن SoapLoginApi يعتمد على HttpClient
builder.Services.AddScoped<ILoginApi, SoapLoginApi>();
builder.Services.AddSession();
builder.Services.AddScoped<IAllowedUsersStore, SqliteAllowedUsersStore>();
builder.Services.AddScoped<IAllowedUsersAdmin, AllowedUsersAdminService>();
builder.Services.AddScoped<IDelegationService, DelegationService>();
// -------------------------
// ✅ SOAP client
// -------------------------
builder.Services.AddScoped<EmployeeSoapClient>();

// -------------------------
// ✅ Alpeta client (HttpClient)
// -------------------------
builder.Services.AddHttpClient<AlpetaClient>(http =>
{
    // إذا تحتاج Timeout من config:
    // http.Timeout = TimeSpan.FromSeconds(10);
});

// -------------------------
// ✅ Facade API
// -------------------------
builder.Services.AddScoped<IEmployeeDevicesApi, EmployeeDevicesApi>();
builder.Services.AddHostedService<DelegationWorker>();

var app = builder.Build();

// ✅ Ensure DB created (اختياري، لكن OK)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
