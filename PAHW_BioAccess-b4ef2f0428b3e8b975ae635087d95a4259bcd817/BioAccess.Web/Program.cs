using Terminals.Web.Contracts;
using Microsoft.EntityFrameworkCore;
using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Terminals.Web.Services.Activity;
using Terminals.Web.Services.AllowedUsers;
using Terminals.Web.Services.Auth;
using Terminals.Web.Services.Dashboard;
using Terminals.Web.Services.Delegations;
using Terminals.Web.Services.Employees;
using Terminals.Web.Services.Terminals;
using Terminals.Web.Filters;

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
builder.Services.AddHttpClient(); // Ù…Ù‡Ù… Ù„Ø£Ù† SoapLoginApi ÙŠØ¹ØªÙ…Ø¯ Ø¹Ù„Ù‰ HttpClient
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
builder.Services.AddScoped<DelegationAlpetaSyncService>();
builder.Services.AddScoped<DelegationService>();
builder.Services.AddScoped<IDelegationService, DelegationService>();
// -------------------------
// âœ… SOAP client
// -------------------------
builder.Services.AddScoped<EmployeeSoapClient>();

// -------------------------
// âœ… Alpeta client (HttpClient)
// -------------------------
builder.Services.AddHttpClient<AlpetaClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(15);
});

// -------------------------
// âœ… Facade API
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

// âœ… Ensure DB created (Ø§Ø®ØªÙŠØ§Ø±ÙŠØŒ Ù„ÙƒÙ† OK)
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
