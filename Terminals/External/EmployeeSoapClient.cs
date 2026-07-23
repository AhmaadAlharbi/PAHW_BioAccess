using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Terminals.Web.External
{
    // Reads employee details from the SOAP service and parses the returned XML.
    public class EmployeeSoapClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<EmployeeSoapClient> _logger;

        // SOAP endpoint comes from app settings.
        private string ServiceUrl => _config["SoapService:Url"] ?? "";

        public EmployeeSoapClient(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<EmployeeSoapClient> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        // Send the SOAP request and return the raw XML for later parsing.
        public async Task<string> GetEmployeeByIdRawAsync(int employeeId, CancellationToken ct = default)
        {
            if (employeeId <= 0)
                throw new ArgumentOutOfRangeException(nameof(employeeId), "employeeId لازم يكون رقم موجب");

            if (string.IsNullOrWhiteSpace(ServiceUrl))
                throw new InvalidOperationException("SoapService:Url مو موجود في appsettings.json");

            var soapXml = BuildGetEmployeeByIdSoap(employeeId);

            var content = new StringContent(soapXml, Encoding.UTF8, "text/xml");
            content.Headers.ContentType!.CharSet = "UTF-8";

            var request = new HttpRequestMessage(HttpMethod.Post, ServiceUrl)
            {
                Content = content
            };

            // Some SOAP services require this header even when it is empty.
            request.Headers.Add("SOAPAction", "\"\"");

            HttpResponseMessage response;
            string rawXml;

            try
            {
                response = await _httpClient.SendAsync(request, ct);
                rawXml = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SOAP call failed for employeeId={EmployeeId}", employeeId);
                throw;
            }

            // Optional raw XML logging helps when the SOAP shape changes.
            var debugXml = _config.GetValue<bool>("SoapService:DebugXml", false);
            if (debugXml)
            {
                _logger.LogInformation("SOAP raw xml (employeeId={EmployeeId}): {Xml}", employeeId, rawXml);
            }

            // Return the raw body even on HTTP errors to help debugging.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SOAP non-success status {StatusCode} for employeeId={EmployeeId}",
                    (int)response.StatusCode, employeeId);

                return rawXml;
            }

            // SOAP faults can still come back with HTTP 200.
            if (rawXml.Contains("<Fault>", StringComparison.OrdinalIgnoreCase) ||
                rawXml.Contains(":Fault", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SOAP Fault returned for employeeId={EmployeeId}", employeeId);
                return rawXml;
            }

            return rawXml;
        }

        // Simple helper when only the employee name is needed.
        public string? ParseEmployeeName(string rawXml)
            => ParseField(rawXml, "name");

        // Read the main employee fields used by the UI.
        public (string? Name, string? Department, string? JobTitle) ParseEmployeeSummary(string rawXml)
        {
            var name = ParseField(rawXml, "name");

            var dept = ParseField(rawXml, "departmentName")
                    ?? ParseField(rawXml, "department");

            var title = ParseField(rawXml, "designation")
                     ?? ParseField(rawXml, "designationEn");

            return (name, dept, title);
        }

        // Return all fields so new SOAP properties can be inspected without new parsing code.
        public Dictionary<string, string> DumpEmployeeDetailFields(string rawXml)
        {
            var detail = GetEmployeePhoneDetail(rawXml);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (detail == null) return result;

            foreach (var el in detail.Elements())
            {
                var key = el.Name.LocalName;
                var val = (el.Value ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                    result[key] = val;
            }

            return result;
        }

        // === XML parsing helpers ===

        private XElement? GetEmployeePhoneDetail(string rawXml)
        {
            if (string.IsNullOrWhiteSpace(rawXml)) return null;

            XDocument doc;
            try
            {
                doc = XDocument.Parse(rawXml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid XML returned from SOAP");
                return null;
            }

            // Match by LocalName because SOAP namespaces can change.
            var detail = doc
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("employeePhoneDetail", StringComparison.OrdinalIgnoreCase));

            return detail;
        }

        private string? ParseField(string rawXml, string fieldName)
        {
            var detail = GetEmployeePhoneDetail(rawXml);
            if (detail == null) return null;

            var el = detail
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            var val = el?.Value?.Trim();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        // Build the SOAP body for employee lookup.
        private string BuildGetEmployeeByIdSoap(int employeeId) => $@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""http://ws.pahw.gov.kw/"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:getEmployeeById>
      <employeeId>{employeeId}</employeeId>
    </ws:getEmployeeById>
  </soapenv:Body>
</soapenv:Envelope>";
    }
}
