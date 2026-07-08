namespace H1Stats.Core.Services;

/// <summary>
/// Classifies ward values as outpatient per PRD business rules.
/// Outpatient: NULL, Cleveland, Toowoomba, CLEV, TWBA (case insensitive).
/// </summary>
public static class WardClassification
{
    private static readonly HashSet<string> OutpatientWards = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cleveland", "Toowoomba", "CLEV", "TWBA"
    };

    public static bool IsOutpatient(string? ward) =>
        string.IsNullOrWhiteSpace(ward) || OutpatientWards.Contains(ward.Trim());

    public static bool IsInpatient(string? ward) => !IsOutpatient(ward);
}
