using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IEmployeeDevicesApi
{
    // Task<EmployeeDevicesDto?> GetEmployeeWithDevicesAsync(int employeeId, CancellationToken ct = default);
    Task<EmployeeDevicesScreenDto?> GetEmployeeDevicesScreenAsync(int employeeId, CancellationToken ct = default);

    Task<OperationResult> AssignOneAsync(int employeeId, string terminalId, CancellationToken ct = default);
    Task<OperationResult> UnassignOneAsync(int employeeId, string terminalId, CancellationToken ct = default);
}
