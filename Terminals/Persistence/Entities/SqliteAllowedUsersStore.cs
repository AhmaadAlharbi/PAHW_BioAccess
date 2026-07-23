using Terminals.Web.Contracts;
using Terminals.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Persistence.Entities;

public class SqliteAllowedUsersStore : IAllowedUsersStore
{
    private readonly LocalAppDbContext _db;

    public SqliteAllowedUsersStore(LocalAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsAllowedAsync(int employeeId, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);

        if (user == null) return false;

        if (user.ValidUntil.HasValue && user.ValidUntil.Value.Date < DateTime.Today)
            return false;

        return true;
    }
    public async Task<bool> IsAdminAsync(int employeeId, CancellationToken ct)
    {
        var user = await _db.AllowedUsers
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);

        return user?.IsAdmin == true;
    }

}