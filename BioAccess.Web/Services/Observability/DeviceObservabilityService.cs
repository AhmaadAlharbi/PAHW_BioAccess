using BioAccess.Web.External;
using BioAccess.Web.Services.Terminals;
using BioAccess.Web.ViewModels;
using Microsoft.Extensions.Caching.Memory;

namespace BioAccess.Web.Services.Observability;

public sealed class DeviceObservabilityService
{
    private readonly AlpetaClient _alpeta;
    private readonly RegionMappingService _regions;
    private readonly ILogger<DeviceObservabilityService> _logger;
    private readonly IMemoryCache _cache;
    private const string TerminalsCacheKey = "Observability:Terminals";

    public DeviceObservabilityService(
        AlpetaClient alpeta,
        RegionMappingService regions,
        ILogger<DeviceObservabilityService> logger,
        IMemoryCache cache)
    {
        _alpeta = alpeta;
        _regions = regions;
        _logger = logger;
        _cache = cache;
    }

    public async Task<DevicesObservabilityViewModel> GetDevicesAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var sinceUtc = nowUtc.AddDays(-7);

        var authLogsTask = FetchAuthLogsSafeAsync(sinceUtc, ct);
        var terminalsTask = LoadTerminalsAsync(ct);
        var areaByTerminalIdTask = LoadAreaByTerminalIdAsync(ct);

        await Task.WhenAll(authLogsTask, terminalsTask, areaByTerminalIdTask);

        var authLogs = authLogsTask.Result;
        var terminals = terminalsTask.Result;
        var areaByTerminalId = areaByTerminalIdTask.Result;

        var authByTerminal = authLogs
            .Select(log => new
            {
                Log = log,
                Key = BuildAuthDeviceKey(log)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Log).ToList(), StringComparer.OrdinalIgnoreCase);

        var devices = terminals
            .Select(terminal =>
            {
                var authKey = !string.IsNullOrWhiteSpace(terminal.DeviceId)
                    ? $"id:{NormalizeTerminalId(terminal.DeviceId)}"
                    : NormalizeTerminalName(terminal.DeviceName);
                authByTerminal.TryGetValue(authKey, out var terminalAuthLogs);
                terminalAuthLogs ??= new List<AlpetaClient.TerminalLogEntry>();

                var activityCount = terminalAuthLogs.Count(log => log.IsSuccess);
                var failedAuthCount = terminalAuthLogs.Count(log => !log.IsSuccess);
                var lastSeenUtc = activityCount > 0
                    ? terminalAuthLogs.Where(log => log.IsSuccess).Max(log => (DateTime?)log.Timestamp)
                    : null;
                var lastSeen = lastSeenUtc?.ToLocalTime();
                var topFailedUsers = BuildTopFailedUsers(terminalAuthLogs);
                var lastFailedAtUtc = failedAuthCount > 0
                    ? terminalAuthLogs.Where(log => !log.IsSuccess).Max(log => (DateTime?)log.Timestamp)
                    : null;
                var lastFailedAt = lastFailedAtUtc?.ToLocalTime();

                var status = GetStatus(lastSeenUtc, nowUtc);
                var reason = BuildProblemReason(status, failedAuthCount);
                var terminalId = terminal.DeviceId?.Trim() ?? "";
                areaByTerminalId.TryGetValue(terminalId, out var area);

                return new DeviceHealthItem
                {
                    TerminalId = terminalId,
                    Name = string.IsNullOrWhiteSpace(terminal.DeviceName)
                        ? $"جهاز {terminal.DeviceId?.Trim()}"
                        : terminal.DeviceName.Trim(),
                    Ip = terminal.IPAddress?.Trim() ?? "",
                    Status = status,
                    ErrorCount = 0,
                    ActivityCount = activityCount,
                    FailedAuthCount = failedAuthCount,
                    TopFailedUsers = topFailedUsers,
                    LastSeen = lastSeen,
                    LastFailedAt = lastFailedAt,
                    ProblemReason = reason,
                    ProblemSummary = reason,
                    Area = string.IsNullOrWhiteSpace(area) ? null : area
                };
            })
            .ToList();

        var existingDeviceKeys = devices
            .Select(x => !string.IsNullOrWhiteSpace(x.TerminalId)
                ? $"id:{NormalizeTerminalId(x.TerminalId)}"
                : NormalizeTerminalName(x.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var authOnlyDevices = authByTerminal
            .Where(group => !existingDeviceKeys.Contains(group.Key))
            .Select(group =>
            {
                var terminalAuthLogs = group.Value;
                var terminalName = GetAuthDeviceDisplayName(terminalAuthLogs);
                var activityCount = terminalAuthLogs.Count(log => log.IsSuccess);
                var failedAuthCount = terminalAuthLogs.Count(log => !log.IsSuccess);
                var lastSeenUtc = activityCount > 0
                    ? terminalAuthLogs.Where(log => log.IsSuccess).Max(log => (DateTime?)log.Timestamp)
                    : null;
                var lastSeen = lastSeenUtc?.ToLocalTime();
                var topFailedUsers = BuildTopFailedUsers(terminalAuthLogs);
                var lastFailedAtUtc = failedAuthCount > 0
                    ? terminalAuthLogs.Where(log => !log.IsSuccess).Max(log => (DateTime?)log.Timestamp)
                    : null;
                var lastFailedAt = lastFailedAtUtc?.ToLocalTime();
                var status = GetStatus(lastSeenUtc, nowUtc);
                var reason = BuildProblemReason(status, failedAuthCount);

                return new DeviceHealthItem
                {
                    TerminalId = "",
                    Name = terminalName,
                    Ip = "",
                    Status = status,
                    ErrorCount = 0,
                    ActivityCount = activityCount,
                    FailedAuthCount = failedAuthCount,
                    TopFailedUsers = topFailedUsers,
                    LastSeen = lastSeen,
                    LastFailedAt = lastFailedAt,
                    ProblemReason = reason,
                    ProblemSummary = reason
                };
            })
            .OrderBy(x => GetStatusRank(x.Status))
            .ThenBy(x => x.ActivityCount == 0 ? 0 : 1)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.TerminalId)
            .ToList();

        devices = devices
            .Concat(authOnlyDevices)
            .OrderByDescending(x => x.ActivityCount + x.FailedAuthCount > 0
                ? (double)x.FailedAuthCount / (x.ActivityCount + x.FailedAuthCount)
                : 0.0)
            .ThenByDescending(x => x.FailedAuthCount)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.TerminalId)
            .ToList();

        _logger.LogInformation("OBS_DEVICES_COUNT count={Count}", devices.Count);

        return new DevicesObservabilityViewModel
        {
            Devices = devices,
            TotalDevices = devices.Count,
            ActiveTodayDevices = devices.Count(x => x.Status == "Active Today"),
            ActiveThisWeekDevices = devices.Count(x => x.Status == "Active This Week"),
            NoActivityDevices = devices.Count(x => x.Status == "No Activity"),
            SecurityAlertDevices = devices.Count(x => x.FailedAuthCount >= 5)
        };
    }

    private async Task<List<AlpetaClient.TerminalLogEntry>> FetchAuthLogsSafeAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            var logs = await _alpeta.GetAuthLogsAsync(sinceUtc, ct);
            _logger.LogInformation("OBSERVABILITY_AUTHLOGS_COUNT count={Count}", logs.Count);
            return logs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_AUTHLOGS_LOAD_FAILED");
            return new List<AlpetaClient.TerminalLogEntry>();
        }
    }

    private async Task<List<DTOs.DeviceDto>> LoadTerminalsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TerminalsCacheKey, out List<DTOs.DeviceDto>? cached) && cached != null)
            return cached;

        try
        {
            var terminals = await _alpeta.GetAllDevicesAsync(ct);
            _cache.Set(TerminalsCacheKey, terminals, TimeSpan.FromMinutes(5));
            return terminals;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_TERMINALS_LOAD_FAILED");
            throw;
        }
    }

    private async Task<Dictionary<string, string>> LoadAreaByTerminalIdAsync(CancellationToken ct)
    {
        try
        {
            var regions = await _regions.GetRegionsAsync(ct);
            var regionNameById = regions.ToDictionary(r => r.Id, r => r.Name);

            var mappings = await _regions.GetAllMappingsAsync(ct);

            return mappings
                .Where(m => regionNameById.ContainsKey(m.Value) && !string.IsNullOrWhiteSpace(regionNameById[m.Value]))
                .ToDictionary(m => m.Key, m => regionNameById[m.Value], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_AREAS_LOAD_FAILED");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeTerminalName(string? value)
        => (value ?? "").Trim().ToLowerInvariant();

    private static string BuildAuthDeviceKey(AlpetaClient.TerminalLogEntry log)
    {
        var terminalId = NormalizeTerminalId(log.TerminalId);
        if (!string.IsNullOrWhiteSpace(terminalId))
            return $"id:{terminalId}";

        var terminalName = NormalizeTerminalName(log.Message);
        if (!string.IsNullOrWhiteSpace(terminalName))
            return terminalName;

        return "";
    }

    private static string GetAuthDeviceDisplayName(IReadOnlyList<AlpetaClient.TerminalLogEntry> logs)
    {
        var terminalName = logs
            .Select(x => x.Message?.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(terminalName))
            return terminalName;

        var terminalId = logs
            .Select(x => NormalizeTerminalId(x.TerminalId))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(terminalId))
            return $"جهاز {terminalId}";

        return "جهاز غير معروف";
    }

    private static string NormalizeTerminalId(string? value)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (compact.Length == 0)
            return "";

        if (compact.All(char.IsDigit))
        {
            var trimmedDigits = compact.TrimStart('0');
            return trimmedDigits.Length > 0 ? trimmedDigits : "0";
        }

        var digitsOnly = new string(compact.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length > 0 && digitsOnly.Length == compact.Length)
        {
            var trimmedDigits = digitsOnly.TrimStart('0');
            return trimmedDigits.Length > 0 ? trimmedDigits : "0";
        }

        return compact;
    }

    private static List<FailedUserEntry> BuildTopFailedUsers(List<AlpetaClient.TerminalLogEntry> logs)
        => logs
            .Where(log => !log.IsSuccess && !string.IsNullOrWhiteSpace(log.UserId))
            .GroupBy(log => log.UserId)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => new FailedUserEntry
            {
                UserId = g.Key,
                UserName = g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.UserName))?.UserName ?? "",
                Count = g.Count()
            })
            .ToList();

    private static string GetStatus(DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (!lastSeenUtc.HasValue)
            return "No Activity";

        var age = nowUtc - lastSeenUtc.Value;

        if (age.TotalHours <= 24)
            return "Active Today";

        if (age.TotalDays <= 7)
            return "Active This Week";

        return "No Activity";
    }

    private static int GetStatusRank(string status) => status switch
    {
        "No Activity" => 0,
        "Active This Week" => 1,
        _ => 2
    };

    private static string BuildProblemReason(string status, int failedAuthCount) => status switch
    {
        "Active Today" => "نشط اليوم",
        "Active This Week" => "نشط هذا الأسبوع",
        _ => failedAuthCount >= 5
            ? "⚠️ ارتفاع في المحاولات الفاشلة"
            : "⚠️ لا يوجد استخدام خلال الـ 7 أيام الماضية"
    };
}
