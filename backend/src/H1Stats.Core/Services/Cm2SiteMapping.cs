namespace H1Stats.Core.Services;

/// <summary>
/// Maps CM2 campus codes: TEST.SITE = SCHEDULE_SITE.SCHEDULE_SITE_RID.
/// Aliases such as TWBA / CLEV / GSL are accepted on the API filter.
/// </summary>
public static class Cm2SiteMapping
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "Greenslopes",
        ["2"] = "Cleveland",
        ["3"] = "Toowoomba",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GSL"] = "1",
        ["GREENSLOPES"] = "1",
        ["CLEV"] = "2",
        ["CLEVELAND"] = "2",
        ["TWBA"] = "3",
        ["TOOWOOMBA"] = "3",
    };

    public static IReadOnlyList<(string Code, string DisplayName)> All =>
        ByCode
            .OrderBy(kvp => int.TryParse(kvp.Key, out var n) ? n : int.MaxValue)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

    public static string ResolveDisplayName(string? raw)
    {
        var code = ResolveCode(raw);
        if (code is null)
            return string.IsNullOrWhiteSpace(raw) ? "All sites" : raw.Trim();

        return ByCode.TryGetValue(code, out var name) ? name : $"Unknown – Site {code}";
    }

    /// <summary>Resolves a filter value (code, name, or alias) to a TEST.SITE code, or null for All.</summary>
    public static string? ResolveCode(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var key = NormalizeCode(filter.Trim());
        if (ByCode.ContainsKey(key))
            return key;

        if (Aliases.TryGetValue(key, out var aliasCode))
            return aliasCode;

        var byName = ByCode.FirstOrDefault(kvp =>
            string.Equals(kvp.Value, filter.Trim(), StringComparison.OrdinalIgnoreCase));
        return byName.Key;
    }

    private static string NormalizeCode(string raw)
    {
        var trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return ((int)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return trimmed;
    }
}
