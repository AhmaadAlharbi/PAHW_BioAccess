using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using BioAccess.Web.DTOs;

namespace BioAccess.Web.External;

// Low-level client for Alpeta login, device list, and user-terminal assignment.
public class AlpetaClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    // Alpeta uses a UUID token after login for later requests.
    private string? _uuid;

    // True when the device list came from local cache instead of live Alpeta data.
    public bool LastCallUsedFallback { get; private set; }

    public AlpetaClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
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
        if (!string.IsNullOrWhiteSpace(_uuid))
        {
            ApplyUuidHeaders(_uuid);
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

            using var res = await _http.PostAsync(loginUrl, content, ct);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            _uuid = doc.RootElement.GetProperty("AccountInfo").GetProperty("Uuid").GetString();
            if (string.IsNullOrWhiteSpace(_uuid))
                throw new Exception("Login succeeded but AccountInfo.Uuid is empty.");

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

        ct.ThrowIfCancellationRequested();

        await EnsureLoggedInAsync(ct);

        using var res = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", ct);

        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _uuid = null;
            await EnsureLoggedInAsync(ct);

            using var retryRes = await _http.GetAsync($"{BaseUrl}/terminals?offset=0&limit=500", ct);
            retryRes.EnsureSuccessStatusCode();

            var retryJson = await retryRes.Content.ReadAsStringAsync(ct);
            using var retryDoc = JsonDocument.Parse(retryJson);
            return ParseDevices(retryDoc.RootElement);
        }

        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return ParseDevices(doc.RootElement);
    }

    // Load the terminals linked to one employee from Alpeta.
    public async Task<List<DeviceDto>> GetEmployeeDevicesAsync(
        int employeeId,
        IReadOnlyCollection<DeviceDto>? allDevices = null,
        CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/users/{userId}/terminaluser";

        using var res = await _http.GetAsync(url, ct);

        // Missing user in Alpeta means no linked terminals.
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

            // Match returned terminal IDs with the full device list for names and location.
            var all = allDevices ?? await GetAllDevicesAsync(ct);
            var byId = all
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
                .GroupBy(d => d.DeviceId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Alpeta response names are not stable, so terminal IDs are extracted loosely.
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
                    // Keep the terminal visible even if it was not in the main device list.
                    list.Add(new DeviceDto
                    {
                        DeviceId = id,
                        DeviceName = $"Terminal {id}",
                        Location = ""
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

    // Assign one employee to one terminal in Alpeta.
    public async Task<bool> AssignUserToTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return false;

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{terminalId.Trim()}/users/{userId}";

        // Some Alpeta versions expect a JSON body even when empty.
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync(url, content, ct);

        // Treat "already assigned" as success for idempotent behavior.
        if (res.StatusCode == HttpStatusCode.Conflict)
            return true;

        return res.IsSuccessStatusCode;
    }

    // Remove one employee from one terminal in Alpeta.
    public async Task<bool> UnassignUserFromTerminalAsync(string terminalId, int employeeId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return false;

        var userId = FormatUserId(employeeId);
        var url = $"{BaseUrl.TrimEnd('/')}/terminals/{terminalId.Trim()}/users/{userId}";

        using var res = await _http.DeleteAsync(url, ct);

        // Treat "not linked" as success so repeated calls stay safe.
        if (res.StatusCode == HttpStatusCode.NotFound)
            return true;

        return res.IsSuccessStatusCode;
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

    private static List<DeviceDto> ParseDevices(JsonElement root)
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

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                list.Add(new DeviceDto { DeviceId = id.Trim(), DeviceName = name, Location = location });
            }
        }

        return list;
    }

    private void ApplyUuidHeaders(string uuid)
    {
        _http.DefaultRequestHeaders.Remove("Uuid");
        _http.DefaultRequestHeaders.Remove("UUID");
        _http.DefaultRequestHeaders.Add("Uuid", uuid);
        _http.DefaultRequestHeaders.Add("UUID", uuid);
    }

}
