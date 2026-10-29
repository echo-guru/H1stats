namespace H1Stats.Core.Services;

/// <summary>Maps CM2 Oracle CARDIOLOGIST1 IDs to display names.</summary>
public static class Cm2CardiologistMapping
{
    public const string UndefinedId = "1";
    public const string UndefinedDisplayName = "Undefined Dr";

    /// <summary>Worklist filter token: patients with no assigned H1 cardiologist (null/0/undefined).</summary>
    public const string UnassignedFilterId = "unassigned";
    public const string UnassignedDisplayName = "Unassigned";

    public static bool IsUnassignedFilter(string? filter) =>
        string.Equals(filter?.Trim(), UnassignedFilterId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filter?.Trim(), UnassignedDisplayName, StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string DisplayName, string Initials)> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        [UndefinedId] = (UndefinedDisplayName, "?"),
        ["981"] = ("Dr Jeremy Wright", "JW"),
        ["282"] = ("Dr Julie Ch'ng", "JC"),
        ["1941"] = ("Dr Justin Morze", "JM"),
        ["144"] = ("Dr Roess Pascoe", "RDP"),
        ["3581"] = ("Dr Roland Hilling-Smith", "RHS"),
        ["2901"] = ("Dr Shi Yi Goo", "SYG"),
        ["1681"] = ("Dr Vance Manins", "VM"),
    };

    /// <summary>Column order for Dr Summary (reporting panel).</summary>
    public static readonly IReadOnlyList<string> SummaryColumnIds =
        ["282", "2901", "1681", "1941", "144", "981"];

    /// <summary>
    /// CM2_DOCTOR_RID values for Hearts 1st cardiologists when they appear as referring doctors.
    /// Distinct from USER_LIST / TEST.CARDIOLOGIST1 ids.
    /// Ch'ng, Pascoe, Ura, Wright, Manins, Morze, Goo. Hilling-Smith is no longer H1.
    /// </summary>
    public static readonly IReadOnlyList<int> ReferringDoctorExcludeCm2Rids =
        [521, 2491, 3329, 3524, 7288, 28301, 63941];

    public static string ReferringDoctorExcludeSql() =>
        $"AND (d.CM2_DOCTOR_RID IS NULL OR d.CM2_DOCTOR_RID NOT IN ({string.Join(", ", ReferringDoctorExcludeCm2Rids)}))";

    private static readonly Dictionary<string, string> IdByDisplayName =
        ById.ToDictionary(kvp => kvp.Value.DisplayName, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return ((int)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return trimmed;
    }

    public static bool IsUndefinedId(string? raw) =>
        string.Equals(NormalizeId(raw), UndefinedId, StringComparison.OrdinalIgnoreCase);

    public static bool IsUndefinedDisplayName(string? name) =>
        string.Equals(name?.Trim(), UndefinedDisplayName, StringComparison.OrdinalIgnoreCase);

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        var key = NormalizeId(raw);
        return ById.TryGetValue(key, out var mapped) ? mapped.DisplayName : key;
    }

    public static string ResolveInitials(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "?";
        var key = NormalizeId(raw);
        return ById.TryGetValue(key, out var mapped) ? mapped.Initials : key;
    }

    public static IReadOnlyList<(string Id, string DisplayName, string Initials)> SummaryColumns =>
        SummaryColumnIds
            .Where(id => ById.ContainsKey(id))
            .Select(id => (id, ById[id].DisplayName, ById[id].Initials))
            .ToList();

    public static string? ResolveOracleId(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        var trimmed = filter.Trim();
        var normalized = NormalizeId(trimmed);
        if (ById.ContainsKey(normalized)) return normalized;
        if (ById.ContainsKey(trimmed)) return trimmed;
        return IdByDisplayName.TryGetValue(trimmed, out var id) ? id : trimmed;
    }

    public static IReadOnlyList<(string Id, string DisplayName)> All =>
        ById.Select(kvp => (Id: kvp.Key, DisplayName: kvp.Value.DisplayName))
            .OrderBy(x => IsUndefinedId(x.Id) ? 1 : 0)
            .ThenBy(x => x.DisplayName)
            .ToList();
}
