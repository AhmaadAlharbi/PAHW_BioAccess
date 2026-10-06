using Terminals.Web.Contracts;
using Terminals.Web.DTOs;

namespace Terminals.Web.Services.Auth;

public sealed class DevelopmentLoginApi : ILoginApi
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevelopmentLoginApi> _logger;

    public DevelopmentLoginApi(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DevelopmentLoginApi> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public Task<LoginResponseDto> LoginAsync(string empId, string password, CancellationToken ct = default)
    {
        if (!_environment.IsDevelopment())
        {
            return Task.FromResult(Fail("Development login is not available."));
        }

        var configuredPassword = _configuration["DevelopmentAdmin:Password"];
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            _logger.LogWarning("Development login attempted but DevelopmentAdmin:Password is not configured.");
            return Task.FromResult(Fail("تسجيل دخول التطوير غير مهيأ."));
        }

        var employeeId = _configuration.GetValue<int>("DevelopmentAdmin:EmployeeId");
        if (!string.Equals(empId, employeeId.ToString(), StringComparison.Ordinal) ||
            !string.Equals(password, configuredPassword, StringComparison.Ordinal))
        {
            return Task.FromResult(Fail("الرقم الوظيفي أو كلمة المرور غير صحيحة."));
        }

        return Task.FromResult(new LoginResponseDto
        {
            ResultCode = 1,
            Message = "Development login successful.",
            SessionKey = $"development-{Guid.NewGuid():N}",
            EmployeeName = _configuration["DevelopmentAdmin:FullName"] ?? "مستخدم التطوير"
        });
    }

    private static LoginResponseDto Fail(string message) => new()
    {
        ResultCode = 0,
        Message = message
    };
}
