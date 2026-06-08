namespace BioAccess.Web.DTOs;

public record AllowedUserDto(
    int EmployeeId,
    string FullName,
    string Email,
    string Department
);
