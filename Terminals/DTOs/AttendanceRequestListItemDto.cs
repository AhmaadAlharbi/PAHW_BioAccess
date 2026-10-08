namespace Terminals.Web.DTOs;

public record AttendanceRequestListItemDto(
    int Id,
    int EmployeeId,
    string EmployeeName,
    DateTime AttendanceDate,
    int CreatedByEmployeeId,
    string CreatedByName,
    string Status,
    DateTime CreatedAt
);
