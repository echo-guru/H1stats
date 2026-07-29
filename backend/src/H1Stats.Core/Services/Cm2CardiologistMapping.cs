namespace H1Stats.Core.Services;

/// <summary>Maps CM2 Oracle CARDIOLOGIST1 IDs to display names.</summary>
public static class Cm2CardiologistMapping
{
    private static readonly Dictionary<string, (string DisplayName, string Initials)> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["981"] = ("Dr Jeremy Wright", "JW"),
        ["282"] = ("Dr Julie Ch'ng", "JC"),
        ["1941"] = ("Dr Justin Morze", "JM"),
        ["144"] = ("Dr Roess Pascoe", "RDP"),
        ["2901"] = ("Dr Shi Yi Goo", "SYG"),
        ["1681"] = ("Dr Vance Manins", "VM"),
    };

    private static readonly Dictionary<string, string> IdByDisplayName =
        ById.ToDictionary(kvp => kvp.Value.DisplayName, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        var key = raw.Trim();
        return ById.TryGetValue(key, out var mapped) ? mapped.DisplayName : key;
    }

    public static string? ResolveOracleId(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        var trimmed = filter.Trim();
        if (ById.ContainsKey(trimmed)) return trimmed;
        return IdByDisplayName.TryGetValue(trimmed, out var id) ? id : trimmed;
    }

    public static IReadOnlyList<(string Id, string DisplayName)> All =>
        ById.Select(kvp => (kvp.Key, kvp.Value.DisplayName))
            .OrderBy(x => x.DisplayName)
            .ToList();
}
