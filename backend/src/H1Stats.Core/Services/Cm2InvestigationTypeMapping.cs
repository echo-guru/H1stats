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

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown";

        var key = raw.Trim();
        if (ByCode.TryGetValue(key, out var name))
            return name;

        return $"Unknown – Code {key}";
    }
}
