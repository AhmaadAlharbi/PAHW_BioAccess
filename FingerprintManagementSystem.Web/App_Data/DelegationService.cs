using FingerprintManagementSystem.ApiAdapter.Persistence;
using FingerprintManagementSystem.ApiAdapter.Persistence.Entities;
using FingerprintManagementSystem.Contracts;
using FingerprintManagementSystem.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FingerprintManagementSystem.ApiAdapter.Implementations;

public class DelegationService : IDelegationService
{
    private readonly LocalAppDbContext _db;
    private readonly IActivityLogService _activity;

    public DelegationService(LocalAppDbContext db, IActivityLogService activity)
    {
        _db = db;
        _activity = activity;
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

        // (اختياري) ضبط البداية على بداية اليوم
        startDate = startDate.Date;

        // ✅ النهاية
#if DEBUG
        endDate = DateTime.Now.AddMinutes(2); // اختبار: ينتهي بعد دقيقتين
#else
        endDate = endDate.Date.AddDays(1);    // إنتاج: نهاية اليوم المختار
#endif

        var now = DateTime.Now;

        // ✅ مهم: حدد الحالة حسب الوقت
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

        var employeeText = string.IsNullOrWhiteSpace(employeeName)
            ? $"الموظف رقم {employeeId}"
            : $"{employeeName} ({employeeId})";

        await _activity.LogAsync(
            action: "Delegation.Created",
            entityType: "Delegation",
            entityId: d.Id.ToString(),
            summary: $"تم إنشاء انتداب لـ {employeeText} ({terminalIds.Count} أجهزة) من {startDate:yyyy-MM-dd} إلى {endDate:yyyy-MM-dd}.",
            details: new { employeeId, employeeName, terminalCount = terminalIds.Count, startDate, endDate, status },
            ct: ct
        );

        return status;
    }
}
