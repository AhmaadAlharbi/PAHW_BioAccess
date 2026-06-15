using System.Text.RegularExpressions;
using System.Text.Json;
using BioAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BioAccess.Web.Services.Restrictions;

public sealed class DeviceRestrictionResult
{
    public bool IsRestricted { get; set; }
    public string Reason { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class DeviceRestrictionService
{
    private static readonly Regex EmployeeIdRegex = new(@"\(" + @"(?<employeeId>\d+)" + @"\)", RegexOptions.Compiled);

    private readonly LocalAppDbContext _db;
    private readonly ILogger<DeviceRestrictionService> _logger;
    private readonly Dictionary<string, DeviceRestrictionResult> _analysisCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HashSet<string>> _assignedByUser = new();
    private readonly Dictionary<int, HashSet<string>> _behaviorByUser = new();
    private RestrictionCatalog? _catalog;

    public DeviceRestrictionService(
        LocalAppDbContext db,
        ILogger<DeviceRestrictionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void PrimeEmployeeAssignments(int userId, IEnumerable<string>? terminalIds)
    {
        if (userId <= 0 || terminalIds is null)
            return;

        _assignedByUser[userId] = terminalIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<DeviceRestrictionResult> AnalyzeAsync(int userId, int terminalId, AlpetaRequestContext context, CancellationToken ct)
    {
        if (userId <= 0 || terminalId <= 0)
            return new DeviceRestrictionResult();

        var terminalKey = terminalId.ToString();
        var cacheKey = $"{userId}:{terminalKey}";
        if (_analysisCache.TryGetValue(cacheKey, out var cached))
            return cached;

        EnsureCatalogLoaded(context);

        var result =
            FindCatalogRestriction(_catalog?.AccessGroups, _catalog?.TerminalLinks, userId, terminalKey, "AccessGroup", "مرتبط بمجموعة صلاحيات (Access Group)") ??
            FindCatalogRestriction(_catalog?.Groups, _catalog?.TerminalLinks, userId, terminalKey, "Group", "مرتبط بمجموعة (Group)") ??
            FindCatalogRestriction(_catalog?.Privileges, _catalog?.TerminalLinks, userId, terminalKey, "Privilege", "مرتبط بصلاحيات (Privilege)") ??
            await FindBehaviorRestrictionAsync(userId, terminalKey, context, ct) ??
            new DeviceRestrictionResult();

        if (result.IsRestricted)
        {
            _logger.LogWarning(
                "RESTRICTED_DEVICE detected terminal={terminalId} source={source}",
                terminalId,
                result.Source);
        }

        _analysisCache[cacheKey] = result;
        return result;
    }

    private void EnsureCatalogLoaded(AlpetaRequestContext context)
    {
        if (_catalog != null)
            return;

        _catalog = new RestrictionCatalog
        {
            AccessGroups = BuildMemberships(context.AccessGroups, "AccessGroup"),
            Groups = BuildMemberships(context.Groups, "Group"),
            Privileges = BuildMemberships(context.Privileges, "Privilege"),
            TerminalLinks = BuildTerminalLinks(context.Terminals)
        };
    }

    private DeviceRestrictionResult? FindCatalogRestriction(
        IReadOnlyCollection<RestrictionMembership>? memberships,
        IReadOnlyDictionary<string, TerminalRestrictionLinks>? terminalLinks,
        int userId,
        string terminalId,
        string source,
        string reason)
    {
        if (memberships is null || memberships.Count == 0)
            return null;

        var userKey = userId.ToString();
        if (memberships.Any(x => x.UserIds.Contains(userKey) && x.TerminalIds.Contains(terminalId)))
        {
            return new DeviceRestrictionResult
            {
                IsRestricted = true,
                Reason = reason,
                Source = source
            };
        }

        if (terminalLinks is null || !terminalLinks.TryGetValue(terminalId, out var links))
            return null;

        var userMembershipIds = memberships
            .Where(x => x.UserIds.Contains(userKey))
            .SelectMany(x => x.EntityIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (userMembershipIds.Count == 0)
            return null;

        var linkedIds = source switch
        {
            "AccessGroup" => links.AccessGroupIds,
            "Group" => links.GroupIds,
            "Privilege" => links.PrivilegeIds,
            _ => null
        };

        if (linkedIds is null || linkedIds.Count == 0)
            return null;

        return linkedIds.Overlaps(userMembershipIds)
            ? new DeviceRestrictionResult
            {
                IsRestricted = true,
                Reason = reason,
                Source = source
            }
            : null;
    }

    private async Task<DeviceRestrictionResult?> FindBehaviorRestrictionAsync(int userId, string terminalId, AlpetaRequestContext context, CancellationToken ct)
    {
        if (!_behaviorByUser.TryGetValue(userId, out var restrictedIds))
        {
            var currentAssigned = GetCurrentAssignedIds(userId, context);
            if (currentAssigned.Count == 0)
            {
                restrictedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var logs = await _db.ActivityLogs
                    .AsNoTracking()
                    .Where(x => x.Action == "EmployeeTerminal.Unassigned" &&
                                x.EntityId != null &&
                                x.CreatedAt >= DateTime.Now.AddDays(-30))
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(500)
                    .ToListAsync(ct);

                restrictedIds = logs
                    .Where(x => !string.IsNullOrWhiteSpace(x.EntityId))
                    .Where(x => currentAssigned.Contains(x.EntityId!.Trim()))
                    .Where(x => SummaryMatchesEmployee(x.Summary, userId))
                    .Select(x => x.EntityId!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            _behaviorByUser[userId] = restrictedIds;
        }

        return restrictedIds.Contains(terminalId)
            ? new DeviceRestrictionResult
            {
                IsRestricted = true,
                Reason = "يتم إعادة الربط تلقائياً (إعداد خارجي)",
                Source = "Behavior"
            }
            : null;
    }

    private HashSet<string> GetCurrentAssignedIds(int userId, AlpetaRequestContext context)
    {
        if (_assignedByUser.TryGetValue(userId, out var cached))
            return cached;

        var assigned = context.EmployeeDevices
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
            .Select(x => x.DeviceId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _assignedByUser[userId] = assigned;
        return assigned;
    }

    private static bool SummaryMatchesEmployee(string? summary, int userId)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return false;

        var match = EmployeeIdRegex.Match(summary);
        return match.Success &&
               int.TryParse(match.Groups["employeeId"].Value, out var parsedEmployeeId) &&
               parsedEmployeeId == userId;
    }

    private static List<RestrictionMembership> BuildMemberships(JsonDocument? document, string source)
    {
        var memberships = new List<RestrictionMembership>();
        if (document is null)
            return memberships;

        foreach (var obj in EnumerateObjects(document.RootElement))
        {
            var membership = new RestrictionMembership();
            foreach (var prop in obj.EnumerateObject())
                CollectMembershipSignals(prop.Name, prop.Value, source, membership);

            if (membership.UserIds.Count == 0 &&
                membership.TerminalIds.Count == 0 &&
                membership.EntityIds.Count == 0)
            {
                continue;
            }

            memberships.Add(membership);
        }

        return memberships;
    }

    private static Dictionary<string, TerminalRestrictionLinks> BuildTerminalLinks(JsonDocument? document)
    {
        var result = new Dictionary<string, TerminalRestrictionLinks>(StringComparer.OrdinalIgnoreCase);
        if (document is null)
            return result;

        foreach (var obj in EnumerateObjects(document.RootElement))
        {
            var membership = new RestrictionMembership();
            foreach (var prop in obj.EnumerateObject())
            {
                CollectMembershipSignals(prop.Name, prop.Value, "AccessGroup", membership);
                CollectMembershipSignals(prop.Name, prop.Value, "Group", membership);
                CollectMembershipSignals(prop.Name, prop.Value, "Privilege", membership);
            }

            if (membership.TerminalIds.Count == 0)
                continue;

            foreach (var terminalId in membership.TerminalIds)
            {
                if (!result.TryGetValue(terminalId, out var links))
                {
                    links = new TerminalRestrictionLinks();
                    result[terminalId] = links;
                }

                links.AccessGroupIds.UnionWith(membership.AccessGroupIds);
                links.GroupIds.UnionWith(membership.GroupIds);
                links.PrivilegeIds.UnionWith(membership.PrivilegeIds);
            }
        }

        return result;
    }

    private static void CollectMembershipSignals(string propertyName, JsonElement value, string source, RestrictionMembership membership)
    {
        var normalizedPropertyName = NormalizePropertyName(propertyName);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in value.EnumerateObject())
                    CollectMembershipSignals(child.Name, child.Value, source, membership);
                break;

            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var child in item.EnumerateObject())
                            CollectMembershipSignals(child.Name, child.Value, source, membership);
                    }
                    else
                    {
                        CollectLeafValue(normalizedPropertyName, item, source, membership);
                    }
                }
                break;

            default:
                CollectLeafValue(normalizedPropertyName, value, source, membership);
                break;
        }
    }

    private static void CollectLeafValue(string propertyName, JsonElement value, string source, RestrictionMembership membership)
    {
        if (!TryGetCompactValue(value, out var compactValue))
            return;

        if (IsUserProperty(propertyName))
            membership.UserIds.Add(compactValue);

        if (IsTerminalProperty(propertyName))
            membership.TerminalIds.Add(compactValue);

        if (IsSourceProperty(propertyName, source))
        {
            membership.EntityIds.Add(compactValue);
            if (source == "AccessGroup") membership.AccessGroupIds.Add(compactValue);
            if (source == "Group") membership.GroupIds.Add(compactValue);
            if (source == "Privilege") membership.PrivilegeIds.Add(compactValue);
        }
        else if (IsGenericIdProperty(propertyName))
        {
            membership.EntityIds.Add(compactValue);
        }
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            foreach (var prop in root.EnumerateObject())
            {
                foreach (var nested in EnumerateObjects(prop.Value))
                    yield return nested;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var nested in EnumerateObjects(item))
                    yield return nested;
            }
        }
    }

    private static bool TryGetCompactValue(JsonElement value, out string compactValue)
    {
        compactValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };

        compactValue = compactValue.Trim();
        if (string.IsNullOrWhiteSpace(compactValue))
            return false;

        if (compactValue.Length > 64 || compactValue.Contains(' ') || compactValue.Contains('\n') || compactValue.Contains('\r'))
            return false;

        return true;
    }

    private static string NormalizePropertyName(string propertyName)
        => propertyName
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();

    private static bool IsUserProperty(string propertyName)
        => propertyName.Contains("user") ||
           propertyName.Contains("employee") ||
           propertyName.Contains("member");

    private static bool IsTerminalProperty(string propertyName)
        => propertyName.Contains("terminal") ||
           propertyName.Contains("device");

    private static bool IsSourceProperty(string propertyName, string source)
    {
        var normalizedSource = source.ToLowerInvariant();
        return propertyName.Contains(normalizedSource) ||
               propertyName.Contains($"{normalizedSource}id");
    }

    private static bool IsGenericIdProperty(string propertyName)
        => propertyName == "id" ||
           propertyName.EndsWith("id", StringComparison.Ordinal);

    private sealed class RestrictionCatalog
    {
        public IReadOnlyCollection<RestrictionMembership> AccessGroups { get; init; } = Array.Empty<RestrictionMembership>();
        public IReadOnlyCollection<RestrictionMembership> Groups { get; init; } = Array.Empty<RestrictionMembership>();
        public IReadOnlyCollection<RestrictionMembership> Privileges { get; init; } = Array.Empty<RestrictionMembership>();
        public IReadOnlyDictionary<string, TerminalRestrictionLinks> TerminalLinks { get; init; } =
            new Dictionary<string, TerminalRestrictionLinks>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RestrictionMembership
    {
        public HashSet<string> EntityIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UserIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TerminalIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AccessGroupIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GroupIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PrivilegeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TerminalRestrictionLinks
    {
        public HashSet<string> AccessGroupIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GroupIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PrivilegeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
