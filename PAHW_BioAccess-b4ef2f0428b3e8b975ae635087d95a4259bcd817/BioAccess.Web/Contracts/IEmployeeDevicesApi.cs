using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IEmployeeDevicesApi
{
    // Task<EmployeeDevicesDto?> GetEmployeeWithDevicesAsync(int employeeId, CancellationToken ct = default);
    Task<EmployeeDevicesScreenDto?> GetEmployeeDevicesScreenAsync(int employeeId, CancellationToken ct = default);

    Task<bool> AssignOneAsync(int employeeId, string terminalId, CancellationToken ct = default);
    Task<bool> UnassignOneAsync(int employeeId, string terminalId, CancellationToken ct = default);
}
