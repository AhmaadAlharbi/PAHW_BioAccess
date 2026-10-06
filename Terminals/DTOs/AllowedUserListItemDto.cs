namespace Terminals.Web.DTOs;

public record AllowedUserListItemDto(
    int EmployeeId,
    string FullName,
    string Email,
    string Department,
    string UserType,
    bool IsActive,
    bool IsAdmin,
    DateTime? ValidUntil
);
