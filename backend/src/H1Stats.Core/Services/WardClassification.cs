namespace H1Stats.Core.Services;

/// <summary>
/// Classifies ward values as outpatient per PRD business rules.
/// Syngo outpatient: NULL, Cleveland, Toowoomba, CLEV, TWBA (case insensitive).
/// CM2: ward 1 = Outpatient; 2–41 = Inpatient; else Unknown.
/// </summary>
public static class WardClassification
{
    private static readonly HashSet<string> SyngoOutpatientWards = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cleveland", "Toowoomba", "CLEV", "TWBA"
    };

    /// <summary>Syngo / H1PACS ward rules.</summary>
    public static bool IsOutpatient(string? ward) =>
        string.IsNullOrWhiteSpace(ward) || SyngoOutpatientWards.Contains(ward.Trim());

    public static bool IsInpatient(string? ward) => !IsOutpatient(ward);

    public static bool IsCm2Outpatient(string? ward) =>
        Cm2WardMapping.IsOutpatientCode(ward);

    public static bool IsCm2Inpatient(string? ward) =>
        Cm2WardMapping.IsInpatientCode(ward);
}
