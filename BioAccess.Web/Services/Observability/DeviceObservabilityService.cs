using BioAccess.Web.External;
using BioAccess.Web.ViewModels;

namespace BioAccess.Web.Services.Observability;

public sealed class DeviceObservabilityService
{
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromHours(2);
    private readonly AlpetaClient _alpeta;
    private readonly ILogger<DeviceObservabilityService> _logger;

    public DeviceObservabilityService(
        AlpetaClient alpeta,
        ILogger<DeviceObservabilityService> logger)
    {
        _alpeta = alpeta;
        _logger = logger;
    }

    public async Task<DevicesObservabilityViewModel> GetDevicesAsync(CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var sinceUtc = DateTime.UtcNow.AddDays(-3);
        List<AlpetaClient.TerminalLogEntry> authLogs;
        try
        {
            authLogs = await _alpeta.GetAuthLogsAsync(sinceUtc, ct);
            _logger.LogInformation("OBSERVABILITY_AUTHLOGS_COUNT count={Count}", authLogs.Count);
            _logger.LogInformation("OBS_AUTH_COUNT count={Count}", authLogs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_AUTHLOGS_LOAD_FAILED");
            authLogs = new List<AlpetaClient.TerminalLogEntry>();
        }

        List<AlpetaClient.TerminalLogEntry> eventLogs;
        try
        {
            eventLogs = await _alpeta.GetEventLogsAsync(sinceUtc, ct);
            _logger.LogInformation("OBS_EVENT_COUNT count={Count}", eventLogs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_EVENTLOGS_LOAD_FAILED");
            eventLogs = new List<AlpetaClient.TerminalLogEntry>();
        }

        var authByTerminal = authLogs
            .Select(log => new
            {
                Log = log,
                Key = BuildAuthDeviceKey(log)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Log).ToList(), StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("OBS_GROUP_COUNT count={Count}", authByTerminal.Count);
        foreach (var group in authByTerminal.Take(3))
        {
            _logger.LogInformation("OBS_GROUP_KEY key={Key} count={Count}", group.Key, group.Value.Count);
        }

        LogAuthGroups(authByTerminal);

        var terminals = await LoadTerminalsAsync(ct);
        var terminalsByName = terminals
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceName))
            .GroupBy(x => NormalizeTerminalName(x.DeviceName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var devices = terminals
            .Select(terminal =>
            {
                var authKey = NormalizeTerminalName(terminal.DeviceName);
                authByTerminal.TryGetValue(authKey, out var terminalAuthLogs);
                terminalAuthLogs ??= new List<AlpetaClient.TerminalLogEntry>();

                LogTerminalMapping(terminal, authKey, terminalAuthLogs);

                var activityCount = terminalAuthLogs.Count;
                var errorCount = 0;
                var lastSeen = terminalAuthLogs
                    .Select(log => (DateTime?)log.Timestamp.ToLocalTime())
                    .OrderByDescending(x => x)
                    .FirstOrDefault();

                var isOffline = IsOffline(terminal.TerminalStatus, terminal.IsOnline, lastSeen, now);
                var status = GetStatus(isOffline, errorCount, activityCount);

                return new DeviceHealthItem
                {
                    TerminalId = terminal.DeviceId?.Trim() ?? "",
                    Name = string.IsNullOrWhiteSpace(terminal.DeviceName)
                        ? $"Terminal {terminal.DeviceId?.Trim()}"
                        : terminal.DeviceName.Trim(),
                    Ip = terminal.IPAddress?.Trim() ?? "",
                    Status = status,
                    ErrorCount = errorCount,
                    ActivityCount = activityCount,
                    LastSeen = lastSeen,
                    IsOffline = isOffline,
                    ProblemReason = BuildProblemReason(status, terminal.IsOnline, lastSeen),
                    ProblemSummary = BuildProblemSummary(status, terminal.IsOnline, lastSeen)
                };
            })
            .ToList();

        var existingDeviceKeys = devices
            .Select(x => NormalizeTerminalName(x.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var authOnlyDevices = authByTerminal
            .Where(group => !existingDeviceKeys.Contains(group.Key))
            .Select(group =>
            {
                var terminalAuthLogs = group.Value;
                var terminalName = GetAuthDeviceDisplayName(terminalAuthLogs);
                var activityCount = terminalAuthLogs.Count;
                var lastSeen = terminalAuthLogs
                    .Select(log => (DateTime?)log.Timestamp.ToLocalTime())
                    .OrderByDescending(x => x)
                    .FirstOrDefault();
                var isOffline = IsOffline(null, null, lastSeen, now);
                var status = GetStatus(isOffline, 0, activityCount);

                LogTerminalMapping(null, group.Key, terminalAuthLogs);

                return new DeviceHealthItem
                {
                    TerminalId = "",
                    Name = terminalName,
                    Ip = "",
                    Status = status,
                    ErrorCount = 0,
                    ActivityCount = activityCount,
                    LastSeen = lastSeen,
                    IsOffline = isOffline,
                    ProblemReason = BuildProblemReason(status, null, lastSeen),
                    ProblemSummary = BuildProblemSummary(status, null, lastSeen)
                };
            })
            .OrderBy(x => GetStatusRank(x.Status))
            .ThenBy(x => x.ActivityCount == 0 ? 0 : 1)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.TerminalId)
            .ToList();

        devices = devices
            .Concat(authOnlyDevices)
            .OrderBy(x => GetStatusRank(x.Status))
            .ThenBy(x => x.ActivityCount == 0 ? 0 : 1)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.TerminalId)
            .ToList();

        _logger.LogInformation("OBS_DEVICES_COUNT count={Count}", devices.Count);

        return new DevicesObservabilityViewModel
        {
            Devices = devices,
            TotalDevices = devices.Count,
            HealthyDevices = devices.Count(x => x.Status == "Healthy"),
            WarningDevices = devices.Count(x => x.Status == "Warning"),
            ProblemDevices = devices.Count(x => x.Status == "Problem")
        };
    }

    private async Task<List<DTOs.DeviceDto>> LoadTerminalsAsync(CancellationToken ct)
    {
        try
        {
            return await _alpeta.GetAllDevicesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBSERVABILITY_TERMINALS_LOAD_FAILED");
            return new List<DTOs.DeviceDto>();
        }
    }

    private void LogAuthGroups(IReadOnlyDictionary<string, List<AlpetaClient.TerminalLogEntry>> authByTerminal)
    {
        var sample = authByTerminal
            .OrderByDescending(x => x.Value.Count)
            .Take(10)
            .Select(x => $"{x.Key}:{x.Value.Count}")
            .ToArray();

        _logger.LogInformation(
            "OBSERVABILITY_AUTHLOG_GROUPS count={Count} sample={Sample}",
            authByTerminal.Count,
            sample.Length == 0 ? "<none>" : string.Join(", ", sample));
    }

    private void LogTerminalMapping(
        DTOs.DeviceDto? terminal,
        string authKey,
        IReadOnlyList<AlpetaClient.TerminalLogEntry> terminalAuthLogs)
    {
        var sampleLogTerminalName = terminalAuthLogs
            .Select(x => x.Message)
            .FirstOrDefault();

        _logger.LogInformation(
            "OBSERVABILITY_TERMINAL_MAPPING deviceId={DeviceId} deviceName={DeviceName} authKey={AuthKey} activityCount={ActivityCount} sampleLogTerminalName={SampleLogTerminalName}",
            terminal?.DeviceId ?? "<none>",
            terminal?.DeviceName ?? "<none>",
            authKey,
            terminalAuthLogs.Count,
            string.IsNullOrWhiteSpace(sampleLogTerminalName) ? "<none>" : sampleLogTerminalName);
    }

    private static string NormalizeTerminalName(string? value)
        => (value ?? "").Trim().ToLowerInvariant();

    private static string BuildAuthDeviceKey(AlpetaClient.TerminalLogEntry log)
    {
        var terminalName = NormalizeTerminalName(log.Message);
        if (!string.IsNullOrWhiteSpace(terminalName))
            return terminalName;

        var terminalId = NormalizeTerminalId(log.TerminalId);
        if (!string.IsNullOrWhiteSpace(terminalId))
            return $"id:{terminalId}";

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
            return $"Terminal {terminalId}";

        return "Unknown Terminal";
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

    private static bool IsOffline(int? terminalStatus, bool? isOnline, DateTime? lastSeen, DateTime now)
    {
        if (terminalStatus.HasValue && terminalStatus.Value != 0)
            return true;

        return false;
    }

    private static string GetStatus(bool isOffline, int errorCount, int activityCount)
    {
        if (isOffline)
            return "Offline";

        return "Healthy";
    }

    private static int GetStatusRank(string status) => status switch
    {
        "Offline" => 0,
        "Problem" => 1,
        "Warning" => 2,
        _ => 3
    };

    private static string BuildProblemSummary(
        string status,
        bool? isOnline,
        DateTime? lastSeen)
    {
        return BuildProblemReason(status, isOnline, lastSeen);
    }

    private static string BuildProblemReason(string status, bool? isOnline, DateTime? lastSeen)
    {
        if (status == "Offline")
            return "Device not connected to server";

        if (isOnline.HasValue && !isOnline.Value)
            return "Connected but network issue detected (ping failed)";

        return lastSeen.HasValue
            ? "Device active"
            : "Device connected";
    }
}
