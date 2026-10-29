namespace H1Stats.Core.Services;

/// <summary>
/// Maps CM2 campus codes used in the UI and diary: SCHEDULE_SITE.SCHEDULE_SITE_RID.
/// Aliases such as TWBA / CLEV / GSL are accepted on the API filter.
/// <para>
/// Completed studies store campus on TEST.SITE, but those values are not the same
/// codes as SCHEDULE_SITE_RID for Cleveland and Toowoomba (2 and 3 are reversed).
/// Use <see cref="ToTestSiteCode"/> / <see cref="FromTestSiteCode"/> when reading or
/// filtering TEST.SITE. Diary/room queries should keep using <see cref="ResolveCode"/>.
/// </para>
/// </summary>
public static class Cm2SiteMapping
{
    public const string Greenslopes = "1";
    public const string Cleveland = "2";
    public const string Toowoomba = "3";

    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        [Greenslopes] = "Greenslopes",
        [Cleveland] = "Cleveland",
        [Toowoomba] = "Toowoomba",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GSL"] = Greenslopes,
        ["GREENSLOPES"] = Greenslopes,
        ["CLEV"] = Cleveland,
        ["CLEVELAND"] = Cleveland,
        ["TWBA"] = Toowoomba,
        ["TOOWOOMBA"] = Toowoomba,
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

    /// <summary>
    /// Resolves a filter value (diary code, name, or alias) to a SCHEDULE_SITE_RID, or null for All.
    /// </summary>
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

    /// <summary>
    /// Converts a diary campus (SCHEDULE_SITE_RID) to the TEST.SITE value stored on completed studies.
    /// Cleveland (2) and Toowoomba (3) are reversed on TEST.SITE.
    /// </summary>
    public static string? ToTestSiteCode(string? diarySiteCode)
    {
        var code = ResolveCode(diarySiteCode);
        return code is null ? null : SwapClevelandToowoomba(code);
    }

    /// <summary>
    /// Converts a TEST.SITE value to the diary campus (SCHEDULE_SITE_RID) used in the UI.
    /// Cleveland (2) and Toowoomba (3) are reversed on TEST.SITE.
    /// </summary>
    public static string? FromTestSiteCode(string? testSiteCode)
    {
        if (string.IsNullOrWhiteSpace(testSiteCode))
            return null;

        return SwapClevelandToowoomba(NormalizeCode(testSiteCode.Trim()));
    }

    private static string SwapClevelandToowoomba(string code) => code switch
    {
        Cleveland => Toowoomba,
        Toowoomba => Cleveland,
        _ => code,
    };

    private static string NormalizeCode(string raw)
    {
        var trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return ((int)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return trimmed;
    }
}
