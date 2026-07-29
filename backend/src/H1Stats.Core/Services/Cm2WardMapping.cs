namespace H1Stats.Core.Services;

/// <summary>Maps CM2 Oracle TEST.WARD codes to display names.</summary>
public static class Cm2WardMapping
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0"] = "Unknown",
        ["1"] = "Outpatient",
        ["2"] = "5",
        ["3"] = "11",
        ["4"] = "13",
        ["5"] = "21",
        ["6"] = "23",
        ["7"] = "25",
        ["8"] = "31",
        ["9"] = "33",
        ["10"] = "35",
        ["11"] = "41",
        ["12"] = "43",
        ["13"] = "45",
        ["14"] = "CCU",
        ["15"] = "EC",
        ["16"] = "ICU",
        ["17"] = "FSU",
        ["18"] = "KPU",
        ["19"] = "Rehab",
        ["20"] = "Theatre 8",
        ["21"] = "Day Surgery",
        ["22"] = "Sunnybank Private",
        ["23"] = "Belmont Private",
        ["24"] = "Theatre Admissions",
        ["25"] = "Inpatient",
        ["26"] = "43",
        ["27"] = "37",
        ["28"] = "47",
        ["29"] = "CCL",
        ["30"] = "St Raphael's",
        ["31"] = "St Damien's",
        ["32"] = "St Luke's",
        ["33"] = "St Gabriel's",
        ["34"] = "St Anne's",
        ["35"] = "ICU",
        ["36"] = "Maternity",
        ["37"] = "St Michael's",
        ["38"] = "Day Surgery",
        ["39"] = "EC",
        ["40"] = "12",
        ["41"] = "22",
    };

    /// <summary>Outpatient ward code in TEST.WARD.</summary>
    public const string OutpatientCode = "1";

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown";

        var key = NormalizeCode(raw);
        if (ByCode.TryGetValue(key, out var name))
            return name;

        return "Unmapped";
    }

    /// <summary>
    /// IP/OP bucket: Outpatient (1), Inpatient (2–41), or Unknown (null/0/outside range).
    /// </summary>
    public static Cm2WardBucket ResolveBucket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Cm2WardBucket.Unknown;

        if (!TryParseCode(raw, out var code))
            return Cm2WardBucket.Unknown;

        if (code == 1)
            return Cm2WardBucket.Outpatient;
        if (code >= 2 && code <= 41)
            return Cm2WardBucket.Inpatient;

        return Cm2WardBucket.Unknown;
    }

    public static bool IsOutpatientCode(string? raw) =>
        ResolveBucket(raw) == Cm2WardBucket.Outpatient;

    public static bool IsInpatientCode(string? raw) =>
        ResolveBucket(raw) == Cm2WardBucket.Inpatient;

    private static bool TryParseCode(string raw, out int code)
    {
        code = 0;
        var trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            code = (int)n;
            return true;
        }

        return false;
    }

    private static string NormalizeCode(string raw) =>
        TryParseCode(raw, out var code)
            ? code.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : raw.Trim();
}

public enum Cm2WardBucket
{
    Outpatient,
    Inpatient,
    Unknown
}
