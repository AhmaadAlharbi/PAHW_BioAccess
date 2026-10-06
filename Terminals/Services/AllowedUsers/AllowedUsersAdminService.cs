using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.AllowedUsers;

public class AllowedUsersAdminService : IAllowedUsersAdmin
{
    private readonly LocalAppDbContext _db;
    private readonly EmployeeSoapClient _soap;

    public AllowedUsersAdminService(LocalAppDbContext db, EmployeeSoapClient soap)
    {
        _db = db;
        _soap = soap;
    }

    public async Task<AllowedUserDto?> FetchFromSoapAsync(int employeeId, CancellationToken ct)
    {
        var rawXml = await _soap.GetEmployeeByIdRawAsync(employeeId, ct);
        var (name, dept, _) = _soap.ParseEmployeeSummary(rawXml);
        var fields = _soap.DumpEmployeeDetailFields(rawXml);

        string email = "";
        var emailKeys = new[] { "email", "emailAddress", "mail", "workEmail", "employeeEmail" };

        foreach (var k in emailKeys)
        {
            if (fields.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                email = v.Trim();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(dept) && string.IsNullOrWhiteSpace(email))
            return null;

        return new AllowedUserDto(
            employeeId,
            name ?? "",
            email,
            dept ?? ""
        );
    }

    public async Task<bool> AddAsync(AllowedUserDto dto, DateTime? validUntil, bool isAdmin, CancellationToken ct)
    {
        var exists = await _db.AllowedUsers.AnyAsync(x => x.EmployeeId == dto.EmployeeId, ct);
        if (exists) return false;

        _db.AllowedUsers.Add(new AllowedUser
        {
            EmployeeId = dto.EmployeeId,
            FullName = dto.FullName,
            Email = dto.Email,
            Department = dto.Department,
            UserType = dto.UserType,
            ValidUntil = validUntil,
            IsAdmin = isAdmin,
            IsActive = true
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<AllowedUserListItemDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.AllowedUsers
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.EmployeeId)
            .Select(x => new AllowedUserListItemDto(
                x.EmployeeId,
                x.FullName,
                x.Email,
                x.Department,
                x.UserType,
                x.IsActive,
                x.IsAdmin,
                x.ValidUntil
            ))
            .ToListAsync(ct);

        return rows;
    }

    public Task<AllowedUserListItemDto?> FindAsync(int employeeId, CancellationToken ct)
        => _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => new AllowedUserListItemDto(
                x.EmployeeId,
                x.FullName,
                x.Email,
                x.Department,
                x.UserType,
                x.IsActive,
                x.IsAdmin,
                x.ValidUntil
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<bool> UpdateAdministrativeAsync(int employeeId, string userType, DateTime? validUntil, bool isAdmin, bool isActive, int currentEmployeeId, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);

        if (user == null)
            return false;

        if (!isActive && employeeId == currentEmployeeId)
            return false;

        if ((!isAdmin || !isActive) && user.IsAdmin && user.IsActive)
        {
            var adminCount = await _db.AllowedUsers
                .CountAsync(x => x.IsAdmin && x.IsActive, ct);

            if (adminCount <= 1)
                return false;
        }

        user.UserType = userType;
        user.ValidUntil = validUntil;
        user.IsAdmin = isAdmin;
        user.IsActive = isActive;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetActiveAsync(int employeeId, bool isActive, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);

        if (user == null)
            return false;

        if (!isActive && user.IsAdmin)
        {
            var adminCount = await _db.AllowedUsers
                .CountAsync(x => x.IsAdmin && x.IsActive, ct);

            if (adminCount <= 1)
                return false;
        }

        user.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetAdminAsync(int employeeId, bool isAdmin, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);

        if (user == null)
            return false;

        if (!isAdmin && user.IsAdmin && user.IsActive)
        {
            var adminCount = await _db.AllowedUsers
                .CountAsync(x => x.IsAdmin && x.IsActive, ct);

            if (adminCount <= 1)
                return false;
        }

        user.IsAdmin = isAdmin;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int employeeId, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);

        if (user == null)
            return false;

        if (user.IsAdmin)
        {
            var adminCount = await _db.AllowedUsers
                .CountAsync(x => x.IsAdmin && x.IsActive, ct);

            if (adminCount <= 1)
                return false;
        }

        _db.AllowedUsers.Remove(user);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
