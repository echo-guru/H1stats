namespace H1Stats.Core.Services;

/// <summary>Known CM2 diary recall service types (OCC_SERVICE).</summary>
public static class Cm2RecallServiceTypes
{
    /// <summary>Cardiology Recall — pink (255,180,255).</summary>
    public const int CardiologyRecallId = 41;

    /// <summary>Admin Recall — yellow (255,255,0).</summary>
    public const int AdminRecallId = 28;

    public static readonly IReadOnlyList<(int Id, string Title, string ColourHint)> EditableOptions =
    [
        (CardiologyRecallId, "Cardiology Recall", "pink"),
        (AdminRecallId, "Admin Recall", "yellow"),
    ];

    public static bool IsAllowedChange(int serviceTypeId) =>
        EditableOptions.Any(o => o.Id == serviceTypeId);

    public static string? ResolveTitle(int serviceTypeId) =>
        EditableOptions.FirstOrDefault(o => o.Id == serviceTypeId).Title;
}
