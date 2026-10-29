namespace H1Stats.Core.Services;

/// <summary>Maps CM2 Oracle TEST.TEST_REQUIRED codes to semantic investigation names.</summary>
public static class Cm2InvestigationTypeMapping
{
    /// <summary>Arrived scheduled consult bookings (SCHEDULE_GROUP_RID = 5).</summary>
    public const string InitialConsultCode = "consult-initial";
    public const string InitialConsultDisplayName = "Initial Consultation";
    public const string FollowUpConsultCode = "consult-followup";
    public const string FollowUpConsultDisplayName = "Follow-up Consultation";
    public const int ConsultScheduleGroupRid = 5;

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
        [InitialConsultCode] = InitialConsultDisplayName,
        [FollowUpConsultCode] = FollowUpConsultDisplayName,
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

    /// <summary>
    /// James Report defaults: transthoracic echo, exercise stress echo, dobutamine stress echo, and consults.
    /// </summary>
    public static readonly IReadOnlyList<string> JamesDefaultCodes =
        ["1", "2", "3", InitialConsultCode];

    /// <summary>Compact column label for the James Report tables and charts.</summary>
    public static string ShortName(string? raw)
    {
        if (IsFollowUpConsultCode(raw))
            return "Follow-up";
        if (IsInitialConsultCode(raw))
            return "Consults";

        var key = string.IsNullOrWhiteSpace(raw) ? "" : NormalizeCode(raw);
        return key switch
        {
            "1" => "Echo",
            "2" => "SE",
            "3" => "DSE",
            "4" => "TOE",
            "5" => "Rest ECG",
            "6" => "Ex ECG",
            "7" => "Holter",
            "8" => "Event",
            "9" => "BP",
            "10" => "Angio",
            "11" => "CTCA",
            "12" => "MRI",
            _ => ResolveDisplayName(raw),
        };
    }

    public static bool IsImagingReportingCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!int.TryParse(NormalizeCode(raw), out var code)) return false;
        return ImagingReportingCodes.Contains(code);
    }

    public static bool IsInitialConsultCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (IsFollowUpConsultCode(raw)) return false;
        var key = NormalizeCode(raw.Trim());
        return string.Equals(key, InitialConsultCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.Trim(), InitialConsultDisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.Trim(), "Consultation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "C1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "INITIAL", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFollowUpConsultCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var key = NormalizeCode(raw.Trim());
        return string.Equals(key, FollowUpConsultCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw.Trim(), FollowUpConsultDisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "FOLLOWUP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "FOLLOW-UP", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsConsultCode(string? raw) =>
        IsInitialConsultCode(raw) || IsFollowUpConsultCode(raw);

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

    public static IReadOnlyList<(string Code, string DisplayName)> ForReferringTests =>
        ReferringDoctorCodes
            .Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Where(code => ByCode.ContainsKey(code))
            .Select(code => (code, ByCode[code]))
            .ToList();

    public static IReadOnlyList<(string Code, string DisplayName)> ForReferringDoctors =>
        ForReferringTests
            .Append((InitialConsultCode, InitialConsultDisplayName))
            .Append((FollowUpConsultCode, FollowUpConsultDisplayName))
            .ToList();

    /// <summary>Resolves a filter value (code or display name) to a TEST_REQUIRED code, or null for All.</summary>
    public static string? ResolveCode(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var key = NormalizeCode(filter.Trim());
        if (ByCode.ContainsKey(key))
            return key;

        if (IsFollowUpConsultCode(filter))
            return FollowUpConsultCode;

        if (IsInitialConsultCode(filter))
            return InitialConsultCode;

        var byName = ByCode.FirstOrDefault(kvp =>
            string.Equals(kvp.Value, filter.Trim(), StringComparison.OrdinalIgnoreCase));
        return byName.Key;
    }

    /// <summary>
    /// Resolves a referring-doctor activity filter (tests or consult). Rejects Resting ECG.
    /// </summary>
    public static string? ResolveReferringDoctorCode(string? filter)
    {
        if (IsFollowUpConsultCode(filter))
            return FollowUpConsultCode;
        if (IsInitialConsultCode(filter))
            return InitialConsultCode;

        var code = ResolveCode(filter);
        if (code is null) return null;
        if (string.Equals(code, FollowUpConsultCode, StringComparison.OrdinalIgnoreCase))
            return FollowUpConsultCode;
        if (string.Equals(code, InitialConsultCode, StringComparison.OrdinalIgnoreCase))
            return InitialConsultCode;
        return IsReferringDoctorCode(code) ? code : null;
    }

    /// <summary>
    /// Resolves one or more referring-doctor investigation filters (repeated values and comma-separated).
    /// Empty result means All tests.
    /// </summary>
    public static IReadOnlyList<string> ResolveReferringDoctorCodes(IEnumerable<string?>? filters)
    {
        if (filters is null)
            return [];

        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            foreach (var token in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var code = ResolveReferringDoctorCode(token);
                if (code is null || !seen.Add(code))
                    continue;
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>
    /// Resolves James Report test filters (repeated values and comma-separated).
    /// Accepts every known investigation, including Resting ECG and consults.
    /// </summary>
    public static IReadOnlyList<string> ResolveJamesReportCodes(IEnumerable<string?>? filters)
    {
        if (filters is null)
            return [];

        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            foreach (var token in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var code = ResolveCode(token);
                if (code is null || !seen.Add(code))
                    continue;
                codes.Add(code);
            }
        }

        return codes;
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
