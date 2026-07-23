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
                x.IsActive,
                x.IsAdmin,
                x.ValidUntil
            ))
            .ToListAsync(ct);

        return rows;
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
