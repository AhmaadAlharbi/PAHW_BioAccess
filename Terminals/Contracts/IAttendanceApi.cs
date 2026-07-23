using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IAttendanceApi
{
    Task<EmployeeDto?> GetEmployeeByIdAsync(int employeeId, CancellationToken ct = default);

    Task<EmployeeDevicesDto?> GetEmployeeWithDevicesAsync(int employeeId, CancellationToken ct = default);
}
