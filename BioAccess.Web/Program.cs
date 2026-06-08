using BioAccess.Web.Contracts;
using Microsoft.EntityFrameworkCore;
using BioAccess.Web.External;
using BioAccess.Web.Persistence;
using BioAccess.Web.Persistence.Entities;
using BioAccess.Web.Services.Activity;
using BioAccess.Web.Services.AllowedUsers;
using BioAccess.Web.Services.Auth;
using BioAccess.Web.Services.Dashboard;
using BioAccess.Web.Services.Delegations;
using BioAccess.Web.Services.Employees;
using BioAccess.Web.Services.Terminals;
using BioAccess.Web.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<SessionGuardFilter>();
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();


var connectionString = builder.Configuration.GetConnectionString("serverDB")
                       ?? throw new Exception("Missing ConnectionStrings:serverDB");


builder.Services.AddDbContext<LocalAppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("serverDB")));
builder.Services.AddScoped<RegionMappingService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddHttpClient(); // مهم لأن SoapLoginApi يعتمد على HttpClient
// Phase 2 Monolith: DashboardController and EmployeesController use local services directly.
// builder.Services.AddHttpClient<IDashboardApiClient, DashboardApiClient>(client =>
// {
//     client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!);
// });
// builder.Services.AddHttpClient<IEmployeesApiClient, EmployeesApiClient>(client =>
// {
//     client.BaseAddress = new Uri("https://localhost:56497");
// });
builder.Services.AddScoped<ILoginApi, SoapLoginApi>();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddScoped<IAllowedUsersStore, SqliteAllowedUsersStore>();
builder.Services.AddScoped<AllowedUsersAdminService>();
builder.Services.AddScoped<IAllowedUsersAdmin, AllowedUsersAdminService>();
builder.Services.AddScoped<DelegationService>();
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
    http.Timeout = TimeSpan.FromSeconds(15);
});

// -------------------------
// ✅ Facade API
// -------------------------
builder.Services.AddScoped<EmployeeDevicesApi>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<TerminalService>();
builder.Services.AddScoped<IEmployeeDevicesApi, EmployeeDevicesApi>();
builder.Services.AddHostedService<DelegationWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ✅ Ensure DB created (اختياري، لكن OK)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
