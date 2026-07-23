using System.Text;
using Terminals.Web.Contracts;
using Terminals.Web.DTOs.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Terminals.Web.Services.Activity;
using Terminals.Web.Services.AllowedUsers;
using Terminals.Web.Services.Auth;
using Terminals.Web.Services.Dashboard;
using Terminals.Web.Services.Delegations;
using Terminals.Web.Services.Employees;
using Terminals.Web.Services.Observability;
using Terminals.Web.Services.Restrictions;
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
builder.Services.AddHttpClient<SoapLoginApi>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("SoapService:TimeoutSeconds", 10)
    );
});
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
builder.Services.AddScoped<ICurrentUser, CompositeCurrentUser>();

builder.Services.AddAuthentication(options =>
{
    // MVC routes rely on SessionGuardFilter — no default scheme handler needed.
    // "SessionScheme" is intentionally unregistered so the auth middleware is a no-op
    // for MVC requests. API routes opt in explicitly via [Authorize(AuthenticationSchemes = "Bearer")].
    options.DefaultScheme = "SessionScheme";
    options.DefaultChallengeScheme = "SessionScheme";
})
.AddJwtBearer("Bearer", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role"
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiPolicy", policy =>
        policy.AddAuthenticationSchemes("Bearer")
              .RequireAuthenticatedUser());
});

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
// ✅ SOAP client
// -------------------------
builder.Services.AddScoped<EmployeeSoapClient>();

builder.Services.AddHttpClient<AlpetaClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(15);
});

// -------------------------
// ✅ Facade API
// -------------------------
builder.Services.AddScoped<EmployeeDevicesApi>();
builder.Services.AddScoped<DeviceObservabilityService>();
builder.Services.AddScoped<DeviceRestrictionService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<TerminalService>();
builder.Services.AddScoped<IRegionService, TerminalService>();
builder.Services.AddScoped<IEmployeeDevicesApi, EmployeeDevicesApi>();
builder.Services.AddHostedService<DelegationWorker>();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(1);
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse.Fail("تم تجاوز الحد المسموح، حاول لاحقًا."),
            token
        );
    };
});

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

app.UseExceptionHandler(appErr =>
{
    appErr.Run(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 500;

            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("An unexpected error occurred.")
            );
        }
    });
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
