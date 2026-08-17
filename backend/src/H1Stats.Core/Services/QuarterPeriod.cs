namespace H1Stats.Core.Services;

/// <summary>Calendar quarter helpers for CM2 referrer growth reporting.</summary>
public static class QuarterPeriod
{
    public static (int Year, int Quarter, DateOnly From, DateOnly ToExclusive, string Label) Of(int year, int quarter)
    {
        if (quarter is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be 1–4.");

        var fromMonth = (quarter - 1) * 3 + 1;
        var from = new DateOnly(year, fromMonth, 1);
        var toExclusive = from.AddMonths(3);
        return (year, quarter, from, toExclusive, $"Q{quarter} {year}");
    }

    /// <summary>Most recently completed calendar quarter relative to <paramref name="asOf"/>.</summary>
    public static (int Year, int Quarter, DateOnly From, DateOnly ToExclusive, string Label) LastCompleted(
        DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var currentQ = (today.Month - 1) / 3 + 1;
        var year = today.Year;
        var quarter = currentQ - 1;
        if (quarter < 1)
        {
            quarter = 4;
            year--;
        }

        return Of(year, quarter);
    }

    public static (int Year, int Quarter, DateOnly From, DateOnly ToExclusive, string Label) Previous(
        int year, int quarter)
    {
        var q = quarter - 1;
        var y = year;
        if (q < 1)
        {
            q = 4;
            y--;
        }

        return Of(y, q);
    }

    public static bool TryParse(int? year, int? quarter,
        out (int Year, int Quarter, DateOnly From, DateOnly ToExclusive, string Label) current,
        out string? error)
    {
        error = null;
        if (year is null && quarter is null)
        {
            current = LastCompleted();
            return true;
        }

        if (year is null or < 2000 or > 2100)
        {
            current = default;
            error = "Invalid or missing year";
            return false;
        }

        if (quarter is null or < 1 or > 4)
        {
            current = default;
            error = "Invalid or missing quarter (1–4)";
            return false;
        }

        current = Of(year.Value, quarter.Value);
        return true;
    }
}
