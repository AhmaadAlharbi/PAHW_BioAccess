using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Terminals.Web.DTOs;
 
namespace Terminals.Web.External;

// Low-level client for Alpeta login, device list, and user-terminal assignment.
public class AlpetaClient
{
    private sealed class AuthLogItem
    {
        public string TerminalId { get; init; } = "";
        public string TerminalName { get; init; } = "";
        public DateTime EventTime { get; init; }
        public bool IsSuccess { get; init; }
        public string UserId { get; init; } = "";
        public string UserName { get; init; } = "";
    }

    private sealed class EventLogItem
    {
        public string TerminalId { get; init; } = "";
        public DateTime EventTime { get; init; }
        public string Message { get; init; } = "";
        public int Content { get; init; }
    }

    public sealed class TerminalLogEntry
    {
        public string TerminalId { get; init; } = "";
        public DateTime Timestamp { get; init; }
        public string EventType { get; init; } = "";
        public string Message { get; init; } = "";
        public bool IsSuccess { get; init; }
        public string UserId { get; init; } = "";
        public string UserName { get; init; } = "";
    }

    public sealed class AllDevicesSnapshot
    {
        public List<DeviceDto> Devices { get; init; } = new();
        public JsonDocument? TerminalsDocument { get; init; }
    }

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AlpetaClient> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly IMemoryCache _cache;
    private const string UuidCacheKey = "Alpeta:UUID";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // Alpeta uses a UUID token after login for later requests.
    private string? _uuid;

    // True when the device list came from local cache instead of live Alpeta data.
    public bool LastCallUsedFallback { get; private set; }

    public AlpetaClient(
        HttpClient http,
        IConfiguration config,
        ILogger<AlpetaClient> logger,
        IMemoryCache cache)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    private string BaseUrl =>
        _config["Alpeta:BaseUrl"]
        ?? throw new InvalidOperationException("Missing configuration: Alpeta:BaseUrl");
    private string UserId => _config["Alpeta:UserId"]
        ?? throw new InvalidOperationException("Missing configuration: Alpeta:UserId");
    private string Password => _config["Alpeta:Password"]
        ?? throw new InvalidOperationException("Missing configuration: Alpeta:Password");
    private int UserType => int.TryParse(_config["Alpeta:UserType"], out var t) ? t : 2;

    private static string FormatUserId(int employeeId) => employeeId.ToString("D8");

    private static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);
        return cts;
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_uuid))
        {
            ApplyUuidHeaders(_uuid);
            return;
        }

        // Reuse the session token across transient instances via shared cache.
        if (_cache.TryGetValue(UuidCacheKey, out string? cachedUuid) && !string.IsNullOrWhiteSpace(cachedUuid))
        {
            _uuid = cachedUuid;
            ApplyUuidHeaders(_uuid);
            _logger.LogDebug("Using cached Alpeta session.");
            return;
        }

        await _loginLock.WaitAsync(ct);
        try
        {
            // Another request may have already filled the token.
            if (!string.IsNullOrWhiteSpace(_uuid))
            {
                ApplyUuidHeaders(_uuid);
                return;
            }

            var loginUrl = $"{BaseUrl}/login";
            var payload = new { userId = UserId, password = Password, userType = UserType };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            using var timeoutCts = CreateTimeoutTokenSource(ct);
            using var res = await _http.PostAsync(loginUrl, content, timeoutCts.Token);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
            using var doc = JsonDocument.Parse(json);

            _uuid = doc.RootElement.GetProperty("AccountInfo").GetProperty("Uuid").GetString();
            if (string.IsNullOrWhiteSpace(_uuid))
                throw new Exception("Login succeeded but AccountInfo.Uuid is empty.");

            _cache.Set(UuidCacheKey, _uuid, TimeSpan.FromMinutes(30));
            ApplyUuidHeaders(_uuid);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private void ClearCachedSession()
    {
        _uuid = null;
        _cache.Remove(UuidCacheKey);
        _http.DefaultRequestHeaders.Remove("Uuid");
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        // Simple health check used to see if Alpeta is reachable.
        try
        {
            await EnsureLoggedInAsync(ct);
            using var timeoutCts = CreateTimeoutTokenSource(ct);
            var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=1", timeoutCts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<DeviceDto>> GetAllDevicesAsync(CancellationToken ct = default)
        => (await GetAllDevicesSnapshotAsync(ct)).Devices;

    public async Task<AllDevicesSnapshot> GetAllDevicesSnapshotAsync(CancellationToken ct = default)
    {
        LastCallUsedFallback = false;

        ct.ThrowIfCancellationRequested();

        await EnsureLoggedInAsync(ct);

        var (devices, lastDocument) = await FetchAllTerminalsPaginatedAsync(ct);
        _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, scope=all", devices.Count, true);
        return new AllDevicesSnapshot
        {
            Devices = devices,
            TerminalsDocument = lastDocument
        };
    }

    private async Task<(List<DeviceDto> Devices, JsonDocument? LastDocument)> FetchAllTerminalsPaginatedAsync(CancellationToken ct)
    {
        const int pageLimit = 500;
        var allDevices = new List<DeviceDto>();
        JsonDocument? lastDocument = null;
        var offset = 0;

        while (true)
        {
            var url = $"{BaseUrl}/terminals?offset={offset}&limit={pageLimit}";
            var attempt = await TryReadAllDevicesResponseAsync(url, ct);
            var wasSessionExpired = false;

            if (!attempt.Success && attempt.SessionExpired)
            {
                wasSessionExpired = true;
                if (attempt.InvalidPayload)
                    _logger.LogWarning("ALPETA_INVALID_PAYLOAD_TREATED_AS_SESSION_EXPIRED scope=all");
                else
                    _logger.LogWarning("ALPETA_SESSION_EXPIRED scope=all statusCode={StatusCode}", (int)attempt.StatusCode!.Value);

                ClearCachedSession();
                await EnsureLoggedInAsync(ct);
                attempt = await TryReadAllDevicesResponseAsync(url, ct);
            }

            if (!attempt.Success)
            {
                _logger.LogWarning("ALPETA_READ_FAILED scope=all statusCode={StatusCode}",
                    attempt.StatusCode.HasValue ? (int)attempt.StatusCode.Value : -1);
                _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, scope=all", 0, false);
                throw new InvalidOperationException(wasSessionExpired
                    ? "تعذر قراءة الأجهزة من النظام الخارجي بعد تجديد الجلسة."
                    : "تعذر قراءة الأجهزة من النظام الخارجي.");
            }

            allDevices.AddRange(attempt.Devices);
            lastDocument?.Dispose();
            lastDocument = attempt.Document;

            if (attempt.Devices.Count < pageLimit)
                break;

            offset += pageLimit;
        }

        return (allDevices, lastDocument);
    }

    // Load the terminals linked to one employee from Alpeta.
    public async Task<List<DeviceDto>> GetEmployeeDevicesAsync(
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices = null,
        CancellationToken ct = default)
    {
        var result = await TryGetEmployeeDevicesAsync(employeeId, allDevices, ct);
        if (!result.Success)
        {
            _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
            throw new InvalidOperationException($"تعذر قراءة أجهزة الموظف {employeeId} من النظام الخارجي.");
        }

        return result.Devices;
    }

    public async Task<(bool Success, List<DeviceDto> Devices)> TryGetEmployeeDevicesAsync(
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices = null,
        CancellationToken ct = default)
    {
        try
        {
            await EnsureLoggedInAsync(ct);

            var userId = FormatUserId(employeeId);
            var url = $"{BaseUrl.TrimEnd('/')}/users/{userId}/terminaluser";

            var firstAttempt = await TryReadEmployeeDevicesResponseAsync(url, employeeId, allDevices, ct);
            if (firstAttempt.Success)
            {
                _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", firstAttempt.Devices.Count, true, employeeId);
                return (true, firstAttempt.Devices);
            }

        if (firstAttempt.SessionExpired)
        {
            if (firstAttempt.InvalidPayload)
            {
                _logger.LogWarning(
                    "ALPETA_INVALID_PAYLOAD_TREATED_AS_SESSION_EXPIRED employeeId={EmployeeId}",
                    employeeId);
            }
            else
            {
                _logger.LogWarning(
                    "ALPETA_SESSION_EXPIRED employeeId={EmployeeId} statusCode={StatusCode}",
                    employeeId,
                    (int)firstAttempt.StatusCode!.Value);
            }

            ClearCachedSession();
            await EnsureLoggedInAsync(ct);

            var retryAttempt = await TryReadEmployeeDevicesResponseAsync(url, employeeId, allDevices, ct);
                if (retryAttempt.Success)
                {
                    _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", retryAttempt.Devices.Count, true, employeeId);
                    return (true, retryAttempt.Devices);
                }

                _logger.LogWarning(
                    "ALPETA_READ_FAILED employeeId={EmployeeId} statusCode={StatusCode}",
                    employeeId,
                    retryAttempt.StatusCode.HasValue ? (int)retryAttempt.StatusCode.Value : -1);
                _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
                return (false, new List<DeviceDto>());
            }

            _logger.LogWarning(
                "ALPETA_READ_FAILED employeeId={EmployeeId} statusCode={StatusCode}",
                employeeId,
                firstAttempt.StatusCode.HasValue ? (int)firstAttempt.StatusCode.Value : -1);
            _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
            return (false, new List<DeviceDto>());
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "ALPETA_TIMEOUT_READ employeeId={EmployeeId}",
                employeeId);
            _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
            return (false, new List<DeviceDto>());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "ALPETA_READ_FAILED employeeId={EmployeeId}",
                employeeId);
            _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
            return (false, new List<DeviceDto>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ALPETA_READ_FAILED employeeId={EmployeeId}",
                employeeId);
            _logger.LogWarning("DEVICE_READ_RESULT count={count}, success={success}, employeeId={employeeId}", 0, false, employeeId);
            return (false, new List<DeviceDto>());
        }
    }

    public Task<JsonDocument?> GetAccessGroupsDocumentAsync(CancellationToken ct = default)
        => GetCatalogDocumentAsync($"{BaseUrl}/accessGroups?offset=0&limit=500", "accessGroups", ct);

    public Task<JsonDocument?> GetGroupsDocumentAsync(CancellationToken ct = default)
        => GetCatalogDocumentAsync($"{BaseUrl}/groups?offset=0&limit=500", "groups", ct);

    public Task<JsonDocument?> GetPrivilegesDocumentAsync(CancellationToken ct = default)
        => GetCatalogDocumentAsync($"{BaseUrl}/privileges?offset=0&limit=500", "privileges", ct);

    public Task<JsonDocument?> GetTerminalsDocumentAsync(CancellationToken ct = default)
        => GetCatalogDocumentAsync($"{BaseUrl}/terminals?offset=0&limit=500", "terminals", ct);

    public Task<List<TerminalLogEntry>> GetAuthLogsAsync(DateTime sinceUtc, CancellationToken ct = default)
        => GetAuthLogsInternalAsync(sinceUtc, DateTime.UtcNow, ct);

    public Task<List<TerminalLogEntry>> GetEventLogsAsync(DateTime sinceUtc, CancellationToken ct = default)
        => GetEventLogsInternalAsync(sinceUtc, DateTime.UtcNow, ct);

    private async Task<(bool Success, List<DeviceDto> Devices, JsonDocument? Document, HttpStatusCode? StatusCode, bool InvalidPayload, bool SessionExpired)> TryReadAllDevicesResponseAsync(
        string url,
        CancellationToken ct)
    {
        using var timeoutCts = CreateTimeoutTokenSource(ct);
        using var res = await _http.GetAsync(url, timeoutCts.Token);

        if (!res.IsSuccessStatusCode)
            return (false, new List<DeviceDto>(), null, res.StatusCode, false, res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("ALPETA_READ_FAILED scope=all reason=EmptyBody");
            return (false, new List<DeviceDto>(), null, res.StatusCode, false, false);
        }

        var doc = JsonDocument.Parse(json);
        var devices = await ParseDevicesAsync(doc.RootElement, timeoutCts.Token);
        if (!IsValidAllDevicesPayload(doc.RootElement, devices))
        {
            _logger.LogWarning(
                "ALPETA_READ_FAILED scope=all reason=UnexpectedPayload payloadSnippet={PayloadSnippet}",
                CreatePayloadSnippet(json));
            return (false, new List<DeviceDto>(), doc, res.StatusCode, true, true);
        }

        return (true, devices, doc, res.StatusCode, false, false);
    }

    private async Task<(bool Success, List<DeviceDto> Devices, HttpStatusCode? StatusCode, bool InvalidPayload, bool SessionExpired)> TryReadEmployeeDevicesResponseAsync(
        string url,
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices,
        CancellationToken ct)
    {
        using var timeoutCts = CreateTimeoutTokenSource(ct);
        using var res = await _http.GetAsync(url, timeoutCts.Token);

        // Missing user in Alpeta means no linked terminals.
        if (res.StatusCode == HttpStatusCode.NotFound)
            return (true, new List<DeviceDto>(), res.StatusCode, false, false);

        if (!res.IsSuccessStatusCode)
            return (false, new List<DeviceDto>(), res.StatusCode, false, res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning(
                "ALPETA_READ_FAILED employeeId={EmployeeId} reason=EmptyBody",
                employeeId);
            return (false, new List<DeviceDto>(), res.StatusCode, false, false);
        }

        using var doc = JsonDocument.Parse(json);
        if (!IsValidEmployeeDevicesPayload(doc.RootElement))
        {
            _logger.LogWarning(
                "ALPETA_READ_FAILED employeeId={EmployeeId} reason=UnexpectedPayload payloadSnippet={PayloadSnippet}",
                employeeId,
                CreatePayloadSnippet(json));
            return (false, new List<DeviceDto>(), res.StatusCode, true, true);
        }

        // Match returned terminal IDs with the full device list for names and location.
        var all = allDevices ?? await GetAllDevicesAsync(ct);
        var devices = BuildEmployeeDevices(doc.RootElement, all);
        return (true, devices, res.StatusCode, false, false);
    }

    private async Task<JsonDocument?> GetCatalogDocumentAsync(string url, string catalogName, CancellationToken ct)
    {
        try
        {
            await EnsureLoggedInAsync(ct);
            var firstAttempt = await TryReadCatalogDocumentAsync(url, ct);
            if (firstAttempt.Document != null)
                return firstAttempt.Document;

            if (firstAttempt.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "ALPETA_SESSION_EXPIRED scope={Scope} statusCode={StatusCode}",
                    catalogName,
                    (int)firstAttempt.StatusCode.Value);
                ClearCachedSession();
                await EnsureLoggedInAsync(ct);
                var retryAttempt = await TryReadCatalogDocumentAsync(url, ct);
                return retryAttempt.Document;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ALPETA_CATALOG_READ_FAILED scope={Scope}", catalogName);
        }

        return null;
    }

    private async Task<(JsonDocument? Document, HttpStatusCode? StatusCode)> TryReadCatalogDocumentAsync(string url, CancellationToken ct)
    {
        using var timeoutCts = CreateTimeoutTokenSource(ct);
        using var res = await _http.GetAsync(url, timeoutCts.Token);
        if (!res.IsSuccessStatusCode)
            return (null, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(json))
            return (null, res.StatusCode);

        try
        {
            return (JsonDocument.Parse(json), res.StatusCode);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ALPETA_CATALOG_INVALID_JSON");
            return (null, res.StatusCode);
        }
    }

    private async Task<List<TerminalLogEntry>> GetAuthLogsInternalAsync(
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        CancellationToken ct)
    {
        const int authLogLimit = 5000;
        var url = BuildLogUrl("authLogs", startTimeUtc, endTimeUtc, 0, authLogLimit);
        var items = await GetLogsAsync(
            url,
            "authLogs",
            ParseAuthLogs,
            ct);

        if (items.Count >= authLogLimit)
            _logger.LogWarning("AUTHLOGS_TRUNCATED count={Count} limit={Limit} — increase limit or implement pagination", items.Count, authLogLimit);

        var logs = items
            .Select(x => new TerminalLogEntry
            {
                TerminalId = x.TerminalId,
                Timestamp = x.EventTime,
                EventType = "AuthLog",
                Message = x.TerminalName,
                IsSuccess = x.IsSuccess,
                UserId = x.UserId,
                UserName = x.UserName
            })
            .ToList();

        _logger.LogInformation(
            "OBS_AUTH_CLIENT_COUNT count={Count} sampleName={SampleName} sampleId={SampleId}",
            logs.Count,
            logs.Select(x => x.Message).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "<none>",
            logs.Select(x => x.TerminalId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "<none>");

        return logs;
    }

    private async Task<List<TerminalLogEntry>> GetEventLogsInternalAsync(
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        CancellationToken ct)
    {
        var url = BuildLogUrl("logs/event_log", startTimeUtc, endTimeUtc, 0, 500);
        var items = await GetLogsAsync(
            url,
            "eventLogs",
            ParseEventLogs,
            ct);

        return items
            .Select(x => new TerminalLogEntry
            {
                TerminalId = x.TerminalId,
                Timestamp = x.EventTime,
                EventType = "EventLog",
                Message = x.Message
            })
            .ToList();
    }

    private async Task<List<TItem>> GetLogsAsync<TItem>(
        string url,
        string scope,
        Func<JsonElement, List<TItem>> parse,
        CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);

        var firstAttempt = await TryReadLogsAsync(url, scope, parse, ct);
        if (firstAttempt.Success)
            return firstAttempt.Items;

        if (firstAttempt.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "ALPETA_SESSION_EXPIRED scope={Scope} statusCode={StatusCode}",
                scope,
                (int)firstAttempt.StatusCode.Value);

            ClearCachedSession();
            await EnsureLoggedInAsync(ct);

            var retryAttempt = await TryReadLogsAsync(url, scope, parse, ct);
            if (retryAttempt.Success)
                return retryAttempt.Items;
        }

        _logger.LogWarning(
            "ALPETA_LOG_READ_FAILED scope={Scope} statusCode={StatusCode}",
            scope,
            firstAttempt.StatusCode.HasValue ? (int)firstAttempt.StatusCode.Value : -1);

        return new List<TItem>();
    }

    // Assign one employee to one terminal in Alpeta.
    public async Task<OperationResult> AssignUserToTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return OperationResult.Fail("بيانات الطلب غير صحيحة");

        var userId = FormatUserId(employeeId);

        _logger.LogInformation(
            "Assign attempt: terminal={TerminalId}, user={UserId}",
            terminalId,
            userId);

        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{terminalId.Trim()}/users/{userId}";

        try
        {
            // Some Alpeta versions expect a JSON body even when empty.
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var timeoutCts = CreateTimeoutTokenSource(ct);
            using var res = await _http.PostAsync(url, content, timeoutCts.Token);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "Alpeta session expired on assign, re-authenticating. terminal={TerminalId}, user={UserId}",
                    terminalId.Trim(),
                    userId);
                _uuid = null;
                _cache.Remove(UuidCacheKey);
                await EnsureLoggedInAsync(ct);
                using var retryContent = new StringContent("{}", Encoding.UTF8, "application/json");
                using var retryTimeoutCts = CreateTimeoutTokenSource(ct);
                using var retryRes = await _http.PostAsync(url, retryContent, retryTimeoutCts.Token);
                if (retryRes.StatusCode == HttpStatusCode.Conflict) return OperationResult.Ok("الجهاز مربوط مسبقاً");
                return retryRes.IsSuccessStatusCode
                    ? OperationResult.Ok("تمت العملية بنجاح")
                    : OperationResult.Fail("فشل الاتصال بالنظام الخارجي، الرجاء المحاولة لاحقاً");
            }

            // Treat "already assigned" as success for idempotent behavior.
            if (res.StatusCode == HttpStatusCode.Conflict)
                return OperationResult.Ok("الجهاز مربوط مسبقاً");

            return res.IsSuccessStatusCode
                ? OperationResult.Ok("تمت العملية بنجاح")
                : OperationResult.Fail("فشل الاتصال بالنظام الخارجي، الرجاء المحاولة لاحقاً");
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "Alpeta request timed out for assign. terminal={TerminalId}, employeeId={EmployeeId}",
                terminalId.Trim(),
                employeeId);
            return OperationResult.Fail("النظام الخارجي غير متاح حالياً، حاول مرة أخرى");
        }
    }

    // Remove one employee from one terminal in Alpeta.
    public async Task<OperationResult> UnassignUserFromTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return OperationResult.Fail("بيانات الطلب غير صحيحة");

        var trimmedTerminalId = terminalId.Trim();
        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{trimmedTerminalId}/users/{userId}";

        _logger.LogInformation(
            "Unassign attempt: terminal={TerminalId}, employeeId={EmployeeId}, alpetaUserId={AlpetaUserId}, request=DELETE {RequestPath}",
            trimmedTerminalId,
            employeeId,
            userId,
            url);

        var assignedDevicesResult = await TryGetEmployeeDevicesAsync(employeeId, ct: ct);
        if (assignedDevicesResult.Success)
        {
            var isAssignedInAlpeta = assignedDevicesResult.Devices
                .Any(x => string.Equals(x.DeviceId?.Trim(), trimmedTerminalId, StringComparison.OrdinalIgnoreCase));

            _logger.LogInformation(
                "Unassign pre-check: terminal={TerminalId}, employeeId={EmployeeId}, existsInAssignmentList={ExistsInAssignmentList}, assignedCount={AssignedCount}",
                trimmedTerminalId,
                employeeId,
                isAssignedInAlpeta,
                assignedDevicesResult.Devices.Count);
        }
        else
        {
            _logger.LogWarning(
                "Unassign pre-check: failed to load current Alpeta assignments for employeeId={EmployeeId} before deleting terminal={TerminalId}.",
                employeeId,
                trimmedTerminalId);
        }

        try
        {
            using var timeoutCts = CreateTimeoutTokenSource(ct);
            using var res = await _http.DeleteAsync(url, timeoutCts.Token);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "Alpeta session expired on unassign, re-authenticating. terminal={TerminalId}, employeeId={EmployeeId}",
                    trimmedTerminalId,
                    employeeId);
                _uuid = null;
                _cache.Remove(UuidCacheKey);
                await EnsureLoggedInAsync(ct);
                using var retryTimeoutCts = CreateTimeoutTokenSource(ct);
                using var retryRes = await _http.DeleteAsync(url, retryTimeoutCts.Token);
                var retryBody = await retryRes.Content.ReadAsStringAsync(retryTimeoutCts.Token);
                _logger.LogInformation(
                    "Unassign retry response: terminal={TerminalId}, employeeId={EmployeeId}, statusCode={StatusCode}, body={Body}",
                    trimmedTerminalId,
                    employeeId,
                    (int)retryRes.StatusCode,
                    string.IsNullOrWhiteSpace(retryBody) ? "<empty>" : retryBody);
                if (retryRes.StatusCode == HttpStatusCode.NotFound)
                    return OperationResult.Ok("تم فك الربط مسبقاً");
                return retryRes.IsSuccessStatusCode
                    ? OperationResult.Ok("تمت العملية بنجاح")
                    : OperationResult.Fail("فشل الاتصال بالنظام الخارجي، الرجاء المحاولة لاحقاً");
            }

            var body = await res.Content.ReadAsStringAsync(timeoutCts.Token);

            _logger.LogInformation(
                "Unassign response: terminal={TerminalId}, employeeId={EmployeeId}, statusCode={StatusCode}, body={Body}",
                trimmedTerminalId,
                employeeId,
                (int)res.StatusCode,
                string.IsNullOrWhiteSpace(body) ? "<empty>" : body);

            // Treat "not linked" as success so repeated calls stay safe.
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "Unassign treated as success because Alpeta returned 404 NotFound for terminal={TerminalId}, employeeId={EmployeeId}.",
                    trimmedTerminalId,
                    employeeId);
                return OperationResult.Ok("تم فك الربط مسبقاً");
            }

            return res.IsSuccessStatusCode
                ? OperationResult.Ok("تمت العملية بنجاح")
                : OperationResult.Fail("فشل الاتصال بالنظام الخارجي، الرجاء المحاولة لاحقاً");
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "Alpeta request timed out for unassign. terminal={TerminalId}, employeeId={EmployeeId}",
                trimmedTerminalId,
                employeeId);
            return OperationResult.Fail("النظام الخارجي غير متاح حالياً، حاول مرة أخرى");
        }
    }

    // === Response parsing helpers ===

    private static IEnumerable<string> ExtractTerminalIds(JsonElement root)
    {
        // Alpeta can return terminal IDs under different property names and shapes.

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Common list shape used by some Alpeta endpoints.
            if (root.TryGetProperty("TerminalTinyList", out var tiny) && tiny.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tiny.EnumerateArray())
                {
                    var id = ReadAnyTerminalId(item);
                    if (!string.IsNullOrWhiteSpace(id)) yield return id!;
                }
            }

            // Another common list shape.
            if (root.TryGetProperty("Rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var id = ReadAnyTerminalId(row);
                    if (!string.IsNullOrWhiteSpace(id)) yield return id!;
                }
            }

            // Search nested objects too so small API shape changes do not break parsing.
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    foreach (var x in ExtractTerminalIds(prop.Value))
                        yield return x;
                }
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var x in ExtractTerminalIds(item))
                    yield return x;
            }
        }
    }

    private static string? ReadAnyTerminalId(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        JsonElement p;

        // Accept the common key names used by different Alpeta responses.
        if (obj.TryGetProperty("TerminalID", out p) ||
            obj.TryGetProperty("TerminalId", out p) ||
            obj.TryGetProperty("terminalId", out p) ||
            obj.TryGetProperty("ID", out p) ||
            obj.TryGetProperty("Id", out p) ||
            obj.TryGetProperty("id", out p))
        {
            return p.ValueKind switch
            {
                JsonValueKind.String => p.GetString(),
                JsonValueKind.Number => p.TryGetInt32(out var n) ? n.ToString() : p.GetRawText(),
                _ => p.GetRawText()
            };
        }

        return null;
    }

    private static List<DeviceDto> BuildEmployeeDevices(JsonElement root, IReadOnlyCollection<DeviceDto> allDevices)
    {
        var byId = allDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
            .GroupBy(d => d.DeviceId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Alpeta response names are not stable, so terminal IDs are extracted loosely.
        var ids = ExtractTerminalIds(root)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new List<DeviceDto>();

        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var dev))
            {
                list.Add(dev);
            }
            else
            {
                // Keep the terminal visible even if it was not in the main device list.
                list.Add(new DeviceDto
                {
                    DeviceId = id,
                    DeviceName = $"جهاز {id}",
                    Location = ""
                });
            }
        }

        return list;
    }

    private static bool IsValidEmployeeDevicesPayload(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return true;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("TerminalTinyList", out var tiny) && tiny.ValueKind == JsonValueKind.Array)
            return true;

        if (root.TryGetProperty("Rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            return true;

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
                return true;
        }

        return false;
    }

    private static bool IsValidAllDevicesPayload(JsonElement root, List<DeviceDto> devices)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var wrapper in root.EnumerateArray())
            {
                if (wrapper.ValueKind == JsonValueKind.Object &&
                    wrapper.TryGetProperty("TerminalInfo", out var terminalInfo) &&
                    terminalInfo.ValueKind == JsonValueKind.Array)
                {
                    return devices.Count > 0 || terminalInfo.GetArrayLength() == 0;
                }
            }

            return false;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("TerminalList", out var terminals) &&
            terminals.ValueKind == JsonValueKind.Array)
        {
            return devices.Count > 0 || terminals.GetArrayLength() == 0;
        }

        return false;
    }

    private async Task<(bool Success, List<TItem> Items, HttpStatusCode? StatusCode)> TryReadLogsAsync<TItem>(
        string url,
        string scope,
        Func<JsonElement, List<TItem>> parse,
        CancellationToken ct)
    {
        using var timeoutCts = CreateTimeoutTokenSource(ct);
        using var res = await _http.GetAsync(url, timeoutCts.Token);
        if (!res.IsSuccessStatusCode)
            return (false, new List<TItem>(), res.StatusCode);

        var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(json))
            return (true, new List<TItem>(), res.StatusCode);

        try
        {
            using var doc = JsonDocument.Parse(json);
            return (true, parse(doc.RootElement), res.StatusCode);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ALPETA_LOG_INVALID_JSON scope={Scope}", scope);
            return (false, new List<TItem>(), res.StatusCode);
        }
    }

    private string BuildLogUrl(string path, DateTime startTimeUtc, DateTime endTimeUtc, int offset, int limit)
    {
        var start = Uri.EscapeDataString(FormatLogTime(startTimeUtc));
        var end = Uri.EscapeDataString(FormatLogTime(endTimeUtc));
        return $"{BaseUrl}/{path}?startTime={start}&endTime={end}&offset={offset}&limit={limit}";
    }

    private static string FormatLogTime(DateTime valueUtc)
        => valueUtc.ToLocalTime().ToString("yyyy-MM-dd");

    private static List<AuthLogItem> ParseAuthLogs(JsonElement root)
    {
        var items = new List<AuthLogItem>();
        foreach (var authLog in EnumerateAuthLogObjects(root))
        {
            TryReadRequiredString(authLog, "TerminalID", out var terminalId);
            TryReadRequiredString(authLog, "TerminalName", out var terminalName);

            terminalId = terminalId.Trim();
            terminalName = terminalName.Trim();

            if (string.IsNullOrWhiteSpace(terminalId) && string.IsNullOrWhiteSpace(terminalName))
                continue;

            if (!TryReadRequiredDateTime(authLog, "EventTime", out var eventTime))
                continue;

            TryReadRequiredString(authLog, "UserID", out var userId);
            TryReadRequiredString(authLog, "UserName", out var userName);

            items.Add(new AuthLogItem
            {
                TerminalId = terminalId,
                TerminalName = terminalName,
                EventTime = eventTime,
                IsSuccess = TryReadOptionalInt(authLog, "AuthResult") == 0,
                UserId = userId,
                UserName = userName
            });
        }

        return items;
    }

    private static List<EventLogItem> ParseEventLogs(JsonElement root)
    {
        var items = new List<EventLogItem>();

        JsonElement logArray;
        if (root.ValueKind == JsonValueKind.Array)
            logArray = root;
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("EventLogList", out var nested) &&
                 nested.ValueKind == JsonValueKind.Array)
            logArray = nested;
        else
            return items;

        foreach (var eventLog in logArray.EnumerateArray())
        {
            if (eventLog.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryReadRequiredString(eventLog, "TerminalID", out var terminalId))
                continue;

            if (!TryReadRequiredDateTime(eventLog, "EventTime", out var eventTime))
                continue;

            var content = TryReadOptionalInt(eventLog, "Content") ?? 0;

            items.Add(new EventLogItem
            {
                TerminalId = terminalId,
                EventTime = eventTime,
                Content = content
            });
        }

        return items;
    }

    private static bool TryReadRequiredString(JsonElement obj, string propertyName, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(propertyName, out var element))
            return false;

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.ToString(),
            _ => ""
        };

        value = value.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadRequiredDateTime(JsonElement obj, string propertyName, out DateTime value)
    {
        value = default;
        if (!obj.TryGetProperty(propertyName, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.String)
            return DateTime.TryParse(element.GetString(), out value);

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var unixValue))
            {
                // Accept both Unix seconds and Unix milliseconds.
                value = unixValue > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unixValue).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(unixValue).UtcDateTime;
                return true;
            }

            if (double.TryParse(element.ToString(), out var doubleValue))
            {
                var unixValueDouble = Convert.ToInt64(doubleValue);
                value = unixValueDouble > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unixValueDouble).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(unixValueDouble).UtcDateTime;
                return true;
            }
        }

        return DateTime.TryParse(element.ToString(), out value);
    }

    private static IEnumerable<JsonElement> EnumerateAuthLogObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var child in EnumerateAuthLogObjects(item))
                    yield return child;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        if (root.TryGetProperty("AuthLogList", out var authLogs))
        {
            if (authLogs.ValueKind == JsonValueKind.Array)
            {
                foreach (var authLog in authLogs.EnumerateArray())
                {
                    if (authLog.ValueKind == JsonValueKind.Object)
                        yield return authLog;
                }

                yield break;
            }

            if (authLogs.ValueKind == JsonValueKind.Object)
            {
                yield return authLogs;
                yield break;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                foreach (var child in EnumerateAuthLogObjects(prop.Value))
                    yield return child;
            }
        }
    }

    private static string CreatePayloadSnippet(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "<empty>";

        payload = payload.Replace(Environment.NewLine, " ").Replace("\n", " ").Replace("\r", " ").Trim();
        return payload.Length <= 300 ? payload : payload[..300];
    }

    private async Task<List<DeviceDto>> ParseDevicesAsync(JsonElement root, CancellationToken ct)
    {
        var list = new List<DeviceDto>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var wrapper in root.EnumerateArray())
            {
                if (wrapper.ValueKind != JsonValueKind.Object ||
                    !wrapper.TryGetProperty("TerminalInfo", out var terminals) ||
                    terminals.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var t in terminals.EnumerateArray())
                {
                    var id = t.TryGetProperty("ID", out var idEl) ? idEl.ToString() : "";
                    var name = t.TryGetProperty("Name", out var nEl) ? (nEl.GetString() ?? "") : "";
                    var ipAddress = t.TryGetProperty("IPAddress", out var ipEl) ? (ipEl.GetString() ?? "") : "";
                    var terminalStatus = TryReadOptionalInt(t, "Status");

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    list.Add(new DeviceDto
                    {
                        DeviceId = id.Trim(),
                        DeviceName = name,
                        Location = "",
                        IPAddress = ipAddress
                        ,TerminalStatus = terminalStatus
                    });
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("TerminalList", out var terminalList) &&
                 terminalList.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in terminalList.EnumerateArray())
            {
                var id = t.TryGetProperty("ID", out var idEl) ? idEl.ToString() : "";
                var name = t.TryGetProperty("Name", out var nEl) ? (nEl.GetString() ?? "") : "";
                var ipAddress = t.TryGetProperty("IPAddress", out var ipEl) ? (ipEl.GetString() ?? "") : "";
                var terminalStatus = TryReadOptionalInt(t, "Status");

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                list.Add(new DeviceDto
                {
                    DeviceId = id.Trim(),
                    DeviceName = name,
                    Location = "",
                    IPAddress = ipAddress
                    ,TerminalStatus = terminalStatus
                });
            }
        }

        return list;
    }

    private static bool CheckDeviceOnline(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(ip, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task LogTerminalDebugInfoAsync(string terminalId, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CreateTimeoutTokenSource(ct);
            using var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", timeoutCts.Token);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Terminal debug lookup failed for terminal={TerminalId}. StatusCode={StatusCode}",
                    terminalId,
                    (int)res.StatusCode);
                return;
            }

            var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("TerminalList", out var terminals) ||
                terminals.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Terminal debug lookup returned no TerminalList for terminal={TerminalId}.",
                    terminalId);
                return;
            }

            foreach (var terminal in terminals.EnumerateArray())
            {
                var id = terminal.TryGetProperty("ID", out var idEl) ? idEl.ToString()?.Trim() : "";
                if (!string.Equals(id, terminalId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = terminal.TryGetProperty("Name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                var location = terminal.TryGetProperty("Location", out var locationEl) ? (locationEl.GetString() ?? "") : "";
                var ipAddress = terminal.TryGetProperty("IPAddress", out var ipEl) ? (ipEl.GetString() ?? "") : "";
                var registerFlag = ReadDebugProperty(terminal, "RegisterFlag");
                var coreFlag = ReadDebugProperty(terminal, "CoreFlag");
                var remoteAllOptionFlag = ReadDebugProperty(terminal, "RemoteAllOptionFlag");

                _logger.LogInformation(
                    "Terminal debug info: terminal={TerminalId}, name={Name}, location={Location}, ipAddress={IPAddress}, registerFlag={RegisterFlag}, coreFlag={CoreFlag}, remoteAllOptionFlag={RemoteAllOptionFlag}",
                    terminalId,
                    name,
                    location,
                    ipAddress,
                    registerFlag,
                    coreFlag,
                    remoteAllOptionFlag);

                return;
            }

            _logger.LogWarning(
                "Terminal debug lookup did not find terminal={TerminalId} in Alpeta TerminalList.",
                terminalId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Terminal debug lookup failed unexpectedly for terminal={TerminalId}.",
                terminalId);
        }
    }

    private static string ReadDebugProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return "<missing>";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "<null>",
            _ => value.GetRawText()
        };
    }

    private static int? TryReadOptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return null;
    }

    private void ApplyUuidHeaders(string uuid)
    {
        _http.DefaultRequestHeaders.Remove("Uuid");
        _http.DefaultRequestHeaders.Add("Uuid", uuid);
    }

}
