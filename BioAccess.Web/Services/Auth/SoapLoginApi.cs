using BioAccess.Web.Contracts;
using BioAccess.Web.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace BioAccess.Web.Services.Auth;

// Handles login against the SOAP service and cleans old stuck sessions when needed.
public class SoapLoginApi : ILoginApi
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<SoapLoginApi> _logger;

    // Read SOAP settings from config, with safe fallback values.
    private string ServiceUrl => _config["SoapService:Url"] ?? "http://192.168.120.52:8080/PAHWService/service";
    private string ApplicationId => _config["SoapService:ApplicationId"] ?? "6";
    private string UserType => _config["SoapService:UserType"] ?? "1";
    private string OperatingSystem => _config["SoapService:OperatingSystem"] ?? "WINDOWS 10";
    private string BrowserName => _config["SoapService:BrowserName"] ?? "FIREFOX";

    public SoapLoginApi(HttpClient httpClient, IConfiguration config, ILogger<SoapLoginApi> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<LoginResponseDto> LoginAsync(string empId, string password, CancellationToken ct = default)
    {
        // Validate simple input first before calling SOAP.
        if (string.IsNullOrWhiteSpace(empId))
            return Fail("Please enter employee ID.");
        if (string.IsNullOrWhiteSpace(password))
            return Fail("Please enter password.");

        try
        {
            // First try the normal login path.
            var firstAttempt = await ExecuteLoginAsync(empId, password, ct);
            if (firstAttempt.ResultCode == 1)
            {
                firstAttempt.EmployeeName = await TryGetEmployeeNameAsync(empId, ct) ?? empId;
                return firstAttempt;
            }

            // ORA-00001 usually means an old session must be cleared first.
            if (!IsStuckSession(firstAttempt.Message))
                return firstAttempt;

            _logger.LogWarning("Login failed because an old SOAP/Oracle session exists for employee {EmpId}.", empId);
            await CleanupSessionAsync(empId, ct);

            _logger.LogInformation("Retrying login after session cleanup for employee {EmpId}.", empId);
            var secondAttempt = await ExecuteLoginAsync(empId, password, ct);
            if (secondAttempt.ResultCode == 1)
            {
                secondAttempt.EmployeeName = await TryGetEmployeeNameAsync(empId, ct) ?? empId;
                return secondAttempt;
            }

            return secondAttempt;
        }
        catch (Exception ex)
        {
            if (IsStuckSession(ex.Message))
            {
                // Some stuck-session cases come as thrown errors instead of normal responses.
                _logger.LogWarning(ex, "Login threw ORA-00001 because an old SOAP/Oracle session exists for employee {EmpId}.", empId);
                await CleanupSessionAsync(empId, ct);

                _logger.LogInformation("Retrying login after session cleanup for employee {EmpId}.", empId);
                var retry = await ExecuteLoginAsync(empId, password, ct);
                if (retry.ResultCode == 1)
                    retry.EmployeeName = await TryGetEmployeeNameAsync(empId, ct) ?? empId;

                return retry;
            }

            return Fail($"Error: {ex.Message}");
        }
    }

    // Try one SOAP login request and parse the result.
    private async Task<LoginResponseDto> ExecuteLoginAsync(string empId, string password, CancellationToken ct)
    {
        var xml = BuildLoginSoap(empId, password);
        var raw = await PostSoapAsync(xml, ct);

        if (IsSoapFault(raw))
            return ParseSoapFault(raw);

        return ParseLoginResponse(raw);
    }

    // Clear old login rows so the next SOAP login can succeed.
    public async Task CleanupSessionAsync(string empId, CancellationToken ct)
    {
        _logger.LogInformation("Cleaning up old SOAP session for employee {EmpId}.", empId);
        await TryLogoutAsync(empId, ct);

        var connectionString = _config.GetConnectionString("Oracle");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Oracle cleanup skipped for employee {EmpId} because ConnectionStrings:Oracle is missing.", empId);
            return;
        }

        _logger.LogInformation("Starting Oracle SQL session cleanup for employee {EmpId}.", empId);

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SEC$USERS_CONNECTION_DTL WHERE empId = :empId AND applicationId = 6";
        cmd.Parameters.Add(new OracleParameter("empId", empId));

        var deletedRows = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Oracle SQL session cleanup completed for employee {EmpId}. Deleted rows: {DeletedRows}.", empId, deletedRows);
    }

    // Ask SOAP to logout, but do not fail cleanup if this step fails.
    private async Task TryLogoutAsync(string empId, CancellationToken ct)
    {
        try
        {
            var xml = BuildLogoutSoap(empId);
            _ = await PostSoapAsync(xml, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SOAP logout cleanup failed for employee {EmpId}.", empId);
        }
    }

    // Load the employee name after login so the UI can store it in session.
    private async Task<string?> TryGetEmployeeNameAsync(string empId, CancellationToken ct)
    {
        try
        {
            var xml = BuildGetEmployeeByIdSoap(empId);
            var raw = await PostSoapAsync(xml, ct);

            var doc = XDocument.Parse(raw);
            XNamespace ns = "http://ws.pahw.gov.kw/";

            var resp = doc.Descendants(ns + "getEmployeeByIdResponse").FirstOrDefault();
            var detail = resp?.Element(ns + "employeePhoneDetail") ?? resp?.Element("employeePhoneDetail");
            if (detail == null) return null;

            var nameAr = detail.Element(ns + "name")?.Value?.Trim() ?? detail.Element("name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(nameAr)) return nameAr;

            var nameEn = detail.Element(ns + "nameEn")?.Value?.Trim() ?? detail.Element("nameEn")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(nameEn)) return nameEn;

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Send one SOAP request and return the raw XML.
    private async Task<string> PostSoapAsync(string soapXml, CancellationToken ct)
    {
        var content = new StringContent(soapXml, Encoding.UTF8, "text/xml");
        content.Headers.ContentType!.CharSet = "UTF-8";

        var req = new HttpRequestMessage(HttpMethod.Post, ServiceUrl) { Content = content };
        req.Headers.Add("SOAPAction", "\"\"");

        var res = await _httpClient.SendAsync(req, ct);
        return await res.Content.ReadAsStringAsync(ct);
    }

    // Build the SOAP XML bodies used by login and cleanup.
    private string BuildLoginSoap(string empId, string password) => $@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""http://ws.pahw.gov.kw/"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:login>
      <empId>{SecurityElement.Escape(empId)}</empId>
      <password>{SecurityElement.Escape(password)}</password>
      <userType>{UserType}</userType>
      <applicationId>{ApplicationId}</applicationId>
      <clientIdentifier></clientIdentifier>
      <userAgent></userAgent>
      <operatingSystem>{OperatingSystem}</operatingSystem>
      <browserName>{BrowserName}</browserName>
      <ipAddress>127.0.0.1</ipAddress>
    </ws:login>
  </soapenv:Body>
</soapenv:Envelope>";

    private string BuildLogoutSoap(string empId) => $@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""http://ws.pahw.gov.kw/"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:logout>
      <empId>{empId}</empId>
      <applicationId>{ApplicationId}</applicationId>
    </ws:logout>
  </soapenv:Body>
</soapenv:Envelope>";

    private string BuildGetEmployeeByIdSoap(string empId) => $@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""http://ws.pahw.gov.kw/"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:getEmployeeById>
      <employeeId>{empId}</employeeId>
    </ws:getEmployeeById>
  </soapenv:Body>
</soapenv:Envelope>";

    // Parse SOAP XML into app-friendly login results.
    private bool IsSoapFault(string xml) => xml.Contains("<Fault") || xml.Contains("<faultcode");

    private LoginResponseDto ParseSoapFault(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value?.Trim()
                        ?? "Unknown SOAP error";
            return Fail($"SOAP Error: {fault}");
        }
        catch
        {
            return Fail("SOAP Error occurred");
        }
    }

    private LoginResponseDto ParseLoginResponse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://ws.pahw.gov.kw/";

            var resp = doc.Descendants(ns + "loginResponse").FirstOrDefault();
            var login = resp?.Element(ns + "login") ?? resp?.Element("login");
            if (login == null) return Fail("Unexpected response format");

            var sessionKey = login.Element(ns + "sessionKey")?.Value
                          ?? login.Element("sessionKey")?.Value
                          ?? string.Empty;

            var resultCodeStr = login.Element(ns + "resultCode")?.Value
                             ?? login.Element("resultCode")?.Value
                             ?? "0";

            var message = login.Element(ns + "message")?.Value
                       ?? login.Element("message")?.Value
                       ?? string.Empty;

            _ = int.TryParse(resultCodeStr, out var code);

            return new LoginResponseDto
            {
                SessionKey = sessionKey,
                ResultCode = code,
                Message = code == 1 ? "Login successful!" : message
            };
        }
        catch (Exception ex)
        {
            return Fail($"Error: {ex.Message}");
        }
    }

    // Detect the known Oracle error used by the old-session problem.
    private static bool IsStuckSession(string message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase);

    private static LoginResponseDto Fail(string msg) => new()
    {
        ResultCode = 0,
        Message = msg
    };
}
