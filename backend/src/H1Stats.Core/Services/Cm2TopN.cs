namespace H1Stats.Core.Services;

/// <summary>Top-N clamp for CM2 referring-doctor reports.</summary>
public static class Cm2TopN
{
    public const int Default = 50;
    public const int Min = 1;
    public const int Max = 500;

    public static int Clamp(int? topN)
    {
        var n = topN is null or 0 ? Default : topN.Value;
        if (n < Min) return Min;
        if (n > Max) return Max;
        return n;
    }
}
