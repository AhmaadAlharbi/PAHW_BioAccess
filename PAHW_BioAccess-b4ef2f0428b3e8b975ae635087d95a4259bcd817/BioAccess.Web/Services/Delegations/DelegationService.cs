using Terminals.Web.Contracts;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Terminals.Web.Services.Activity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Delegations;

// Handles saving, ending, and canceling local delegation records.
public class DelegationService : IDelegationService
{
    private readonly LocalAppDbContext _db;
    private readonly IActivityLogService _activity;
    private readonly DelegationAlpetaSyncService _alpetaSync;
    private readonly IHttpContextAccessor _http;

    public DelegationService(
        LocalAppDbContext db,
        IActivityLogService activity,
        DelegationAlpetaSyncService alpetaSync,
        IHttpContextAccessor http)
    {
        _db = db;
        _activity = activity;
        _alpetaSync = alpetaSync;
        _http = http;
    }

    public async Task<string> SaveDelegationAsync(
        int employeeId,
        List<string> terminalIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        // Basic validation before saving the delegation.
        if (employeeId <= 0 || terminalIds == null || terminalIds.Count == 0)
            return "Invalid";

        if (endDate < startDate)
            return "Invalid";

        var normalizedTerminalIds = terminalIds
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTerminalIds.Count == 0)
            return "Invalid";

        // Store whole-day ranges so the delegation stays active until end of day.
        startDate = startDate.Date;
        endDate = endDate.Date.AddDays(1);

        var now = DateTime.Now;
        // If the start date is now or earlier, the worker should treat it as active.
        var status = (startDate <= now && endDate > now) ? "Active" : "Scheduled";

        // Save terminal rows under one delegation header.
        var d = new Delegation
        {
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            CreatedAt = now,
            Terminals = normalizedTerminalIds
                .Select(t => new DelegationTerminal { TerminalId = t })
                .ToList()
        };

        _db.Delegations.Add(d);
        await _db.SaveChangesAsync(ct);

        if (status == "Active")
        {
            var snapshotReady = await _alpetaSync.TryCaptureActivationSnapshotAsync(d, now, ct);
            if (snapshotReady)
            {
                await _db.SaveChangesAsync(ct);

                foreach (var terminal in d.Terminals)
                    await _alpetaSync.EnsureAssignedAsync(d.Id, d.EmployeeId, terminal.TerminalId, "manual", ct);
            }
        }

        var employeeName = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

        var employeeText = FormatEmployeeText(employeeName, employeeId);
        var actorText = GetActorText();
        var delegatedTerminalIds = normalizedTerminalIds;

        // Region names are loaded only for the activity log text.
        var regionNames = await _db.TerminalRegionMaps
            .AsNoTracking()
            .Where(x => delegatedTerminalIds.Contains(x.TerminalId))
            .Select(x => x.Region != null ? x.Region.Name : null)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .ToListAsync(ct);

        var regionsLine = regionNames.Count > 0
            ? "\nÙÙŠ Ø§Ù„Ù…Ù†Ø§Ø·Ù‚: " + string.Join("ØŒ ", regionNames)
            : "";

        await _activity.LogAsync(
            action: "Delegation.Created",
            entityType: "Delegation",
            entityId: d.Id.ToString(),
            summary: $"ØªÙ… Ø¥Ù†Ø´Ø§Ø¡ Ø§Ù†ØªØ¯Ø§Ø¨ Ù„Ù„Ù…ÙˆØ¸Ù {employeeText} Ù„Ø¹Ø¯Ø¯ ({delegatedTerminalIds.Count}) Ø£Ø¬Ù‡Ø²Ø©{regionsLine}\nÙ…Ù† {startDate:yyyy-MM-dd} Ø¥Ù„Ù‰ {endDate:yyyy-MM-dd}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}",
            details: new { employeeId, employeeName, terminalCount = delegatedTerminalIds.Count, startDate, endDate, status },
            ct: ct
        );

        return status;
    }

    public async Task<bool> EndActiveDelegationAsync(int employeeId, List<string> terminalIds, CancellationToken ct = default)
    {
        // === Manual delegation end ===
        // Unassign the selected delegated terminals and close empty delegation records.
        var selectedTerminalIds = (terminalIds ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (employeeId <= 0 || selectedTerminalIds.Count == 0) return false;

        // Load only active delegations for this employee.
        var delegations = await _db.Delegations
            .Include(x => x.Terminals)
            .Where(x => x.EmployeeId == employeeId && x.Status == "Active")
            .ToListAsync(ct);

        if (delegations.Count == 0) return false;

        // Match only the terminal rows the user selected on the page.
        var matchedRows = delegations
            .SelectMany(d => (d.Terminals ?? new())
                .Where(t => selectedTerminalIds.Contains((t.TerminalId ?? "").Trim()))
                .Select(t => new { Delegation = d, Terminal = t }))
            .ToList();

        if (matchedRows.Count == 0) return false;

        var successCount = 0;
        var unassignedCount = 0;
        foreach (var row in matchedRows)
        {
            var terminalId = (row.Terminal.TerminalId ?? "").Trim();
            var success = true;
            var coveredByAnotherDelegation = await IsCoveredByAnotherActiveDelegationAsync(
                row.Delegation.Id,
                employeeId,
                terminalId,
                ct);

            if (!row.Terminal.WasAssignedBefore && !coveredByAnotherDelegation)
            {
                success = await _alpetaSync.EnsureUnassignedAsync(
                    row.Delegation.Id,
                    employeeId,
                    terminalId,
                    "manual",
                    ct);
                if (success)
                    unassignedCount++;
            }

            if (!success) continue;

            successCount++;
            row.Delegation.Terminals.Remove(row.Terminal);
            _db.DelegationTerminals.Remove(row.Terminal);
        }

        if (successCount == 0) return false;

        // If a delegation no longer has terminals, mark it as ended.
        var now = DateTime.Now;
        foreach (var delegation in delegations)
        {
            if ((delegation.Terminals?.Count ?? 0) == 0)
            {
                delegation.Status = "ManuallyEnded";
                delegation.ExpiredAt = now;
            }
        }

        var employeeName = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

        var employeeText = FormatEmployeeText(employeeName, employeeId);
        var actorText = GetActorText();

        await _db.SaveChangesAsync(ct);

        var regionNames = await _db.TerminalRegionMaps
            .AsNoTracking()
            .Where(x => selectedTerminalIds.Contains(x.TerminalId))
            .Select(x => x.Region != null ? x.Region.Name : null)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .ToListAsync(ct);

        var regionsLine = regionNames.Count > 0
            ? "\nÙÙŠ Ø§Ù„Ù…Ù†Ø§Ø·Ù‚: " + string.Join("ØŒ ", regionNames)
            : "";

        await _activity.LogAsync(
            action: "Delegation.ManuallyEnded",
            entityType: "Delegation",
            entityId: null,
            summary: $"ØªÙ… Ø¥Ù†Ù‡Ø§Ø¡ Ø§Ù†ØªØ¯Ø§Ø¨ Ø§Ù„Ù…ÙˆØ¸Ù {employeeText} Ù„Ø¹Ø¯Ø¯ ({successCount}) Ø£Ø¬Ù‡Ø²Ø©{regionsLine}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}",
            details: new { employeeId, employeeName, terminalCount = successCount, terminalIds = selectedTerminalIds, unassigned = unassignedCount },
            ct: ct
        );

        return true;
    }

    public async Task<bool> CancelScheduledDelegationAsync(int delegationId, CancellationToken ct = default)
    {
        // Only future delegations can be canceled here.
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
            summary: $"ØªÙ… Ø¥Ù†Ù‡Ø§Ø¡ Ø§Ù†ØªØ¯Ø§Ø¨ Ø§Ù„Ù…ÙˆØ¸Ù {employeeText}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}",
            details: new { delegationId = delegation.Id, employeeId = delegation.EmployeeId, employeeName },
            ct: ct
        );

        return true;
    }

    private string GetActorText()
        // Read the current user from session for audit logs.
        => FormatActorText(_http.HttpContext?.Session.GetString("EmpName"), _http.HttpContext?.Session.GetString("EmpId"));

    private Task<bool> IsCoveredByAnotherActiveDelegationAsync(
        int delegationId,
        int employeeId,
        string terminalId,
        CancellationToken ct)
        => _db.DelegationTerminals.AnyAsync(
            x => x.DelegationId != delegationId &&
                 x.Delegation != null &&
                 x.Delegation.EmployeeId == employeeId &&
                 x.TerminalId == terminalId &&
                 x.Delegation.Status == "Active" &&
                 x.Delegation.StartDate <= DateTime.Now &&
                 x.Delegation.EndDate > DateTime.Now,
            ct);

    private static string FormatEmployeeText(string? employeeName, int employeeId)
        => string.IsNullOrWhiteSpace(employeeName)
            ? $"ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ ({employeeId})"
            : $"{employeeName.Trim()} ({employeeId})";

    private static string FormatActorText(string? actorName, string? actorId)
    {
        var name = string.IsNullOrWhiteSpace(actorName) ? "ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ" : actorName.Trim();
        var id = string.IsNullOrWhiteSpace(actorId) ? "ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ" : actorId.Trim();
        return $"{name} ({id})";
    }
}
