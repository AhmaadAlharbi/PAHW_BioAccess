using BioAccess.Web.Contracts;
using BioAccess.Web.Persistence;
using BioAccess.Web.Persistence.Entities;
using BioAccess.Web.Services.Activity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BioAccess.Web.Services.Delegations;

public class DelegationService : IDelegationService
{
    private readonly LocalAppDbContext _db;
    private readonly IActivityLogService _activity;
    private readonly IEmployeeDevicesApi _api;
    private readonly IHttpContextAccessor _http;

    public DelegationService(LocalAppDbContext db, IActivityLogService activity, IEmployeeDevicesApi api, IHttpContextAccessor http)
    {
        _db = db;
        _activity = activity;
        _api = api;
        _http = http;
    }

    public async Task<string> SaveDelegationAsync(
        int employeeId,
        List<string> terminalIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        if (terminalIds == null || terminalIds.Count == 0)
            return "Invalid";

        if (endDate < startDate)
            return "Invalid";

        startDate = startDate.Date;
        endDate = endDate.Date.AddDays(1);

        var now = DateTime.Now;
        var status = (startDate <= now && endDate > now) ? "Active" : "Scheduled";

        var d = new Delegation
        {
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            CreatedAt = now,
            Terminals = terminalIds
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => new DelegationTerminal { TerminalId = t.Trim() })
                .ToList()
        };

        _db.Delegations.Add(d);
        await _db.SaveChangesAsync(ct);

        var employeeName = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

        var employeeText = FormatEmployeeText(employeeName, employeeId);
        var actorText = GetActorText();
        var delegatedTerminalIds = terminalIds
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var regionNames = await _db.TerminalRegionMaps
            .AsNoTracking()
            .Where(x => delegatedTerminalIds.Contains(x.TerminalId))
            .Select(x => x.Region != null ? x.Region.Name : null)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .ToListAsync(ct);

        var regionsLine = regionNames.Count > 0
            ? "\nفي المناطق: " + string.Join("، ", regionNames)
            : "";

        await _activity.LogAsync(
            action: "Delegation.Created",
            entityType: "Delegation",
            entityId: d.Id.ToString(),
            summary: $"تم إنشاء انتداب للموظف {employeeText} لعدد ({terminalIds.Count}) أجهزة{regionsLine}\nمن {startDate:yyyy-MM-dd} إلى {endDate:yyyy-MM-dd}\nبواسطة: {actorText}",
            details: new { employeeId, employeeName, terminalCount = terminalIds.Count, startDate, endDate, status },
            ct: ct
        );

        return status;
    }

    public async Task<bool> EndActiveDelegationAsync(int delegationId, CancellationToken ct = default)
    {
        if (delegationId <= 0) return false;

        var delegation = await _db.Delegations
            .Include(x => x.Terminals)
            .FirstOrDefaultAsync(x => x.Id == delegationId, ct);

        if (delegation is null) return false;
        if (delegation.Status != "Active") return false;

        var terminals = (delegation.Terminals ?? new())
            .Select(t => (t.TerminalId ?? "").Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var successCount = 0;
        foreach (var terminalId in terminals)
        {
            var success = false;
            for (var i = 1; i <= 3 && !success; i++)
            {
                success = await _api.UnassignOneAsync(delegation.EmployeeId, terminalId, ct);
                if (!success) await Task.Delay(500, ct);
            }

            if (success) successCount++;
        }

        var now = DateTime.Now;
        delegation.Status = "Expired";
        delegation.ExpiredAt = now;

        var employeeName = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == delegation.EmployeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

        var employeeText = FormatEmployeeText(employeeName, delegation.EmployeeId);
        var actorText = GetActorText();

        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Delegation.ManuallyEnded",
            entityType: "Delegation",
            entityId: delegation.Id.ToString(),
            summary: $"تم إنهاء انتداب الموظف {employeeText}\nبواسطة: {actorText}",
            details: new { delegationId = delegation.Id, employeeId = delegation.EmployeeId, employeeName, terminalCount = terminals.Count, unassigned = successCount },
            ct: ct
        );

        return true;
    }

    public async Task<bool> CancelScheduledDelegationAsync(int delegationId, CancellationToken ct = default)
    {
        if (delegationId <= 0) return false;

        var delegation = await _db.Delegations
            .FirstOrDefaultAsync(x => x.Id == delegationId, ct);

        if (delegation is null) return false;
        if (delegation.Status != "Scheduled") return false;

        var now = DateTime.Now;
        delegation.Status = "Cancelled";
        delegation.ExpiredAt = now;
        await _db.SaveChangesAsync(ct);

        var employeeName = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == delegation.EmployeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

        var employeeText = FormatEmployeeText(employeeName, delegation.EmployeeId);
        var actorText = GetActorText();

        await _activity.LogAsync(
            action: "Delegation.Cancelled",
            entityType: "Delegation",
            entityId: delegation.Id.ToString(),
            summary: $"تم إنهاء انتداب الموظف {employeeText}\nبواسطة: {actorText}",
            details: new { delegationId = delegation.Id, employeeId = delegation.EmployeeId, employeeName },
            ct: ct
        );

        return true;
    }

    private string GetActorText()
        => FormatActorText(_http.HttpContext?.Session.GetString("EmpName"), _http.HttpContext?.Session.GetString("EmpId"));

    private static string FormatEmployeeText(string? employeeName, int employeeId)
        => string.IsNullOrWhiteSpace(employeeName)
            ? $"غير معروف ({employeeId})"
            : $"{employeeName.Trim()} ({employeeId})";

    private static string FormatActorText(string? actorName, string? actorId)
    {
        var name = string.IsNullOrWhiteSpace(actorName) ? "غير معروف" : actorName.Trim();
        var id = string.IsNullOrWhiteSpace(actorId) ? "غير معروف" : actorId.Trim();
        return $"{name} ({id})";
    }
}
