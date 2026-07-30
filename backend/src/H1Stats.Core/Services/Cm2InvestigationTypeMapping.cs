namespace H1Stats.Core.Services;

/// <summary>Maps CM2 Oracle TEST.TEST_REQUIRED codes to semantic investigation names.</summary>
public static class Cm2InvestigationTypeMapping
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "Transthoracic Echo",
        ["2"] = "Exercise Stress Echo",
        ["3"] = "Dobutamine Stress Echo",
        ["4"] = "Transoesophageal Echo",
        ["5"] = "Resting ECG",
        ["6"] = "Exercise ECG",
        ["7"] = "Holter Monitor",
        ["8"] = "Seven-Day Event Monitor",
        ["9"] = "Blood Pressure Monitor",
        ["10"] = "Coronary Angiography",
        ["11"] = "CT Coronary Angiography",
        ["12"] = "Cardiac MRI",
    };

    /// <summary>
    /// Imaging investigation codes currently included in Reporting Dr – CM2
    /// (TTE, stress echo, dobutamine, TEE). ECG/Holter/BP excluded as attribution is unreliable.
    /// </summary>
    public static readonly IReadOnlyList<int> ImagingReportingCodes = [1, 2, 3, 4];

    /// <summary>
    /// Investigation types offered on Top Referring Doctors – CM2.
    /// Resting ECG (5) is excluded — not tracked for referrer reporting.
    /// </summary>
    public static readonly IReadOnlyList<int> ReferringDoctorCodes =
        [1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];

    public static bool IsImagingReportingCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!int.TryParse(NormalizeCode(raw), out var code)) return false;
        return ImagingReportingCodes.Contains(code);
    }

    public static bool IsReferringDoctorCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!int.TryParse(NormalizeCode(raw), out var code)) return false;
        return ReferringDoctorCodes.Contains(code);
    }

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown";

        var key = NormalizeCode(raw);
        if (ByCode.TryGetValue(key, out var name))
            return name;

        return $"Unknown – Code {key}";
    }

    public static IReadOnlyList<(string Code, string DisplayName)> All =>
        ByCode
            .OrderBy(kvp => int.TryParse(kvp.Key, out var n) ? n : int.MaxValue)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

    public static IReadOnlyList<(string Code, string DisplayName)> ForReferringDoctors =>
        ReferringDoctorCodes
            .Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Where(code => ByCode.ContainsKey(code))
            .Select(code => (code, ByCode[code]))
            .ToList();

    /// <summary>Resolves a filter value (code or display name) to a TEST_REQUIRED code, or null for All.</summary>
    public static string? ResolveCode(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var key = NormalizeCode(filter.Trim());
        if (ByCode.ContainsKey(key))
            return key;

        var byName = ByCode.FirstOrDefault(kvp =>
            string.Equals(kvp.Value, filter.Trim(), StringComparison.OrdinalIgnoreCase));
        return byName.Key;
    }

    /// <summary>
    /// Resolves a referring-doctor investigation filter. Rejects Resting ECG and unknown codes.
    /// </summary>
    public static string? ResolveReferringDoctorCode(string? filter)
    {
        var code = ResolveCode(filter);
        if (code is null) return null;
        return IsReferringDoctorCode(code) ? code : null;
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
