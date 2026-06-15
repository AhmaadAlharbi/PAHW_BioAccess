using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using BioAccess.Web.DTOs;

namespace BioAccess.Web.External;

// Low-level client for Alpeta login, device list, and user-terminal assignment.
public class AlpetaClient
{
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

    public AlpetaClient(HttpClient http, IConfiguration config, ILogger<AlpetaClient> logger, IMemoryCache cache)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _cache = cache;
    }

    private string BaseUrl => _config["Alpeta:BaseUrl"] ?? "http://192.168.120.56:9004/v1";
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
                return;

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
    {
        LastCallUsedFallback = false;

        ct.ThrowIfCancellationRequested();

        await EnsureLoggedInAsync(ct);

        using var timeoutCts = CreateTimeoutTokenSource(ct);
        using var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", timeoutCts.Token);

        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _uuid = null;
            _cache.Remove(UuidCacheKey);
            await EnsureLoggedInAsync(ct);

            using var retryTimeoutCts = CreateTimeoutTokenSource(ct);
            using var retryRes = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", retryTimeoutCts.Token);
            retryRes.EnsureSuccessStatusCode();

            var retryJson = await retryRes.Content.ReadAsStringAsync(retryTimeoutCts.Token);
            using var retryDoc = JsonDocument.Parse(retryJson);
            return await ParseDevicesAsync(retryDoc.RootElement, retryTimeoutCts.Token);
        }

        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
        using var doc = JsonDocument.Parse(json);
        return await ParseDevicesAsync(doc.RootElement, timeoutCts.Token);
    }

    // Load the terminals linked to one employee from Alpeta.
    public async Task<List<DeviceDto>> GetEmployeeDevicesAsync(
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices = null,
        CancellationToken ct = default)
    {
        var result = await TryGetEmployeeDevicesAsync(employeeId, allDevices, ct);
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

            using var timeoutCts = CreateTimeoutTokenSource(ct);
            using var res = await _http.GetAsync(url, timeoutCts.Token);

            // Missing user in Alpeta means no linked terminals.
            if (res.StatusCode == HttpStatusCode.NotFound)
                return (true, new List<DeviceDto>());

            if (!res.IsSuccessStatusCode)
                return (false, new List<DeviceDto>());

            var json = await res.Content.ReadAsStringAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(json))
                return (false, new List<DeviceDto>());

            using var doc = JsonDocument.Parse(json);

            // Match returned terminal IDs with the full device list for names and location.
            var all = allDevices ?? await GetAllDevicesAsync(ct);
            return (true, BuildEmployeeDevices(doc.RootElement, all));
        }
        catch (JsonException)
        {
            return (false, new List<DeviceDto>());
        }
        catch
        {
            return (false, new List<DeviceDto>());
        }
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
                    DeviceName = $"Terminal {id}",
                    Location = "",
                    IsOnline = false
                });
            }
        }

        return list;
    }

    private async Task<List<DeviceDto>> ParseDevicesAsync(JsonElement root, CancellationToken ct)
    {
        var list = new List<DeviceDto>();

        if (root.TryGetProperty("TerminalList", out var terminals) &&
            terminals.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in terminals.EnumerateArray())
            {
                var id = t.TryGetProperty("ID", out var idEl) ? idEl.ToString() : "";
                var name = t.TryGetProperty("Name", out var nEl) ? (nEl.GetString() ?? "") : "";
                var location = t.TryGetProperty("Location", out var lEl) ? (lEl.GetString() ?? "") : "";
                var ipAddress = t.TryGetProperty("IPAddress", out var ipEl) ? (ipEl.GetString() ?? "") : "";

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                list.Add(new DeviceDto
                {
                    DeviceId = id.Trim(),
                    DeviceName = name,
                    Location = location,
                    IPAddress = ipAddress
                });
            }
        }

        var tasks = list.Select(device =>
            Task.Run(() => device.IsOnline = CheckDeviceOnline(device.IPAddress), ct));

        await Task.WhenAll(tasks);
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

    private void ApplyUuidHeaders(string uuid)
    {
        _http.DefaultRequestHeaders.Remove("Uuid");
        _http.DefaultRequestHeaders.Remove("UUID");
        _http.DefaultRequestHeaders.Add("Uuid", uuid);
        _http.DefaultRequestHeaders.Add("UUID", uuid);
    }

}
