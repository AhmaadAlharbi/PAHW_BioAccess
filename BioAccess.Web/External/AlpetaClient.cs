using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using BioAccess.Web.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BioAccess.Web.External;

public class AlpetaClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _loginLock = new(1, 1);


    // Session UUID بعد Login
    private string? _uuid;
    private const string UuidCacheKey   = "alpeta:uuid";
    private const string DeviceCacheKey = "alpeta:devices:snapshot";

    /// <summary>
    /// True when the last GetAllDevicesAsync call returned data from the local fallback
    /// cache because the live API response was empty or failed.
    /// </summary>
    public bool LastCallUsedFallback { get; private set; }

    public AlpetaClient(HttpClient http, IConfiguration config, IMemoryCache cache)
    {
        _http = http;
        _config = config;
        _cache = cache;
    }

    private string BaseUrl => _config["Alpeta:BaseUrl"] ?? "http://192.168.120.56:9004/v1";
    private string UserId => _config["Alpeta:UserId"]
        ?? throw new InvalidOperationException("Missing configuration: Alpeta:UserId");
    private string Password => _config["Alpeta:Password"]
        ?? throw new InvalidOperationException("Missing configuration: Alpeta:Password");
    private int UserType => int.TryParse(_config["Alpeta:UserType"], out var t) ? t : 2;

    private static string FormatUserId(int employeeId) => employeeId.ToString("D8");

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(UuidCacheKey, out var cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            _uuid = cached;
            _http.DefaultRequestHeaders.Remove("Uuid");
            _http.DefaultRequestHeaders.Remove("UUID");
            _http.DefaultRequestHeaders.Add("Uuid", _uuid);
            _http.DefaultRequestHeaders.Add("UUID", _uuid);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_uuid))
            return;

        await _loginLock.WaitAsync(ct);
        try
        {
            // double-check بعد ما أخذنا lock
            if (_cache.TryGetValue<string>(UuidCacheKey, out cached) &&
                !string.IsNullOrWhiteSpace(cached))
            {
                _uuid = cached;
                _http.DefaultRequestHeaders.Remove("Uuid");
                _http.DefaultRequestHeaders.Remove("UUID");
                _http.DefaultRequestHeaders.Add("Uuid", _uuid);
                _http.DefaultRequestHeaders.Add("UUID", _uuid);
                return;
            }

            // double-check بعد ما أخذنا lock
            if (!string.IsNullOrWhiteSpace(_uuid))
                return;

            var loginUrl = $"{BaseUrl}/login";
            var payload = new { userId = UserId, password = Password, userType = UserType };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            using var res = await _http.PostAsync(loginUrl, content, ct);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            _uuid = doc.RootElement.GetProperty("AccountInfo").GetProperty("Uuid").GetString();
            if (string.IsNullOrWhiteSpace(_uuid))
                throw new Exception("Login succeeded but AccountInfo.Uuid is empty.");

            _http.DefaultRequestHeaders.Remove("Uuid");
            _http.DefaultRequestHeaders.Remove("UUID");
            _http.DefaultRequestHeaders.Add("Uuid", _uuid);
            _http.DefaultRequestHeaders.Add("UUID", _uuid);

            _cache.Set(UuidCacheKey, _uuid, TimeSpan.FromMinutes(20));
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureLoggedInAsync(ct);
            var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=1", ct);
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

        // CancellationToken cancellations must always propagate — don't catch them.
        ct.ThrowIfCancellationRequested();

        try
        {
            await EnsureLoggedInAsync(ct);

            var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", ct);

            // Stale UUID: clear it, re-authenticate once, and retry the request.
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _cache.Remove(UuidCacheKey);
                _uuid = null;
                await EnsureLoggedInAsync(ct);
                res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", ct);
            }

            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var list = new List<DeviceDto>();

            if (doc.RootElement.TryGetProperty("TerminalList", out var terminals) &&
                terminals.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in terminals.EnumerateArray())
                {
                    var id       = t.TryGetProperty("ID",       out var idEl) ? idEl.ToString()        : "";
                    var name     = t.TryGetProperty("Name",     out var nEl)  ? (nEl.GetString()  ?? "") : "";
                    var location = t.TryGetProperty("Location", out var lEl)  ? (lEl.GetString()  ?? "") : "";

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    list.Add(new DeviceDto { DeviceId = id.Trim(), DeviceName = name, Location = location });
                }
            }

            // A non-empty result is the new authoritative snapshot.
            if (list.Count > 0)
            {
                _cache.Set(DeviceCacheKey, list, TimeSpan.FromMinutes(10));
                return list;
            }

            // Alpeta returned 200 with zero devices — treat as a transient API issue
            // and fall through to the last-known-good snapshot below.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Network error, auth failure, JSON error — fall through to cache.
        }

        // Return the most recent snapshot we trust.
        if (_cache.TryGetValue<List<DeviceDto>>(DeviceCacheKey, out var snapshot) && snapshot?.Count > 0)
        {
            LastCallUsedFallback = true;
            return snapshot;
        }

        return new List<DeviceDto>(); // no snapshot yet (first run, never had a good response)
    }

    // ✅ يجيب الأجهزة المرتبطة بالموظف (بشكل robust لأي naming يرجّعه Alpeta)
    public async Task<List<DeviceDto>> GetEmployeeDevicesAsync(
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices = null,
        CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/users/{userId}/terminaluser";

        using var res = await _http.GetAsync(url, ct);

        // الموظف غير موجود في Alpeta = لا أجهزة مرتبطة
        if (res.StatusCode == HttpStatusCode.NotFound)
            return new List<DeviceDto>();

        if (!res.IsSuccessStatusCode)
            return new List<DeviceDto>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            return new List<DeviceDto>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            // 1) جهّز dict للأجهزة الكاملة عشان نرجّع الاسم/الموقع الصحيحين
            var all = allDevices ?? await GetAllDevicesAsync(ct);
            var byId = all
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
                .GroupBy(d => d.DeviceId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 2) استخرج Terminal IDs من أي شكل يرجّعه Alpeta
            var ids = ExtractTerminalIds(doc.RootElement)
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
                    // احتياط لو ما وجدناه في AllDevices
                    list.Add(new DeviceDto
                    {
                        DeviceId = id,
                        DeviceName = $"Terminal {id}",
                        Location = null
                    });
                }
            }

            return list;
        }
        catch (JsonException)
        {
            return new List<DeviceDto>();
        }
    }

    // ✅ Assign user to terminal: POST /terminals/{terminalID}/users/{userID}
    public async Task<bool> AssignUserToTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return false;

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{terminalId.Trim()}/users/{userId}";

        // بعض الإصدارات تحتاج body حتى لو فاضي
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync(url, content, ct);

        // 409 أحيانًا معناها Already exists
        if (res.StatusCode == HttpStatusCode.Conflict)
            return true;

        return res.IsSuccessStatusCode;
    }

    // ✅ Unassign: DELETE /terminals/{terminalID}/users/{userID}
    public async Task<bool> UnassignUserFromTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return false;

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{terminalId.Trim()}/users/{userId}";

        using var res = await _http.DeleteAsync(url, ct);

        // إذا أصلاً مو مربوط، اعتبرها OK
        if (res.StatusCode == HttpStatusCode.NotFound)
            return true;

        return res.IsSuccessStatusCode;
    }

    // ===== Helpers =====

    private static IEnumerable<string> ExtractTerminalIds(JsonElement root)
    {
        // أشكال شائعة:
        // - { TerminalTinyList: [ { TerminalID / TerminalId / ID ... } ] }
        // - { Rows: [ { TerminalId ... } ] }
        // - أحيانًا List تحت مفاتيح أخرى

        if (root.ValueKind == JsonValueKind.Object)
        {
            // جرّب TerminalTinyList
            if (root.TryGetProperty("TerminalTinyList", out var tiny) && tiny.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tiny.EnumerateArray())
                {
                    var id = ReadAnyTerminalId(item);
                    if (!string.IsNullOrWhiteSpace(id)) yield return id!;
                }
            }

            // جرّب Rows
            if (root.TryGetProperty("Rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var id = ReadAnyTerminalId(row);
                    if (!string.IsNullOrWhiteSpace(id)) yield return id!;
                }
            }

            // fallback: دور داخل أي خصائص أخرى (عشان لو Alpeta غيّر الشكل)
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

        // أكثر أسماء شفتها
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

}
