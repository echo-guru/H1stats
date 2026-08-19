using System.Globalization;

namespace H1Stats.Core.Services;

/// <summary>Calendar-month helpers for referrer history and Dr Summary.</summary>
public static class MonthPeriod
{
    public const int MonthCount = 12;

    /// <summary>
    /// Inclusive window covering the current calendar month and the previous 11 months.
    /// <paramref name="toExclusive"/> is the first day of the month after the current month.
    /// </summary>
    public static (
        DateOnly From,
        DateOnly ToExclusive,
        IReadOnlyList<(string YearMonth, string Label, DateOnly From, DateOnly ToExclusive)> Months)
        Prior12(DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var from = currentMonthStart.AddMonths(-(MonthCount - 1));
        var toExclusive = currentMonthStart.AddMonths(1);

        var months = new List<(string YearMonth, string Label, DateOnly From, DateOnly ToExclusive)>(MonthCount);
        var cursor = from;
        var enAu = CultureInfo.GetCultureInfo("en-AU");
        for (var i = 0; i < MonthCount; i++)
        {
            var next = cursor.AddMonths(1);
            months.Add((
                cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                cursor.ToString("MMM yyyy", enAu),
                cursor,
                next));
            cursor = next;
        }

        return (from, toExclusive, months);
    }

    public static (int Year, int Month, DateOnly From, DateOnly ToExclusive, string Label) Of(int year, int month)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1–12.");

        var from = new DateOnly(year, month, 1);
        var toExclusive = from.AddMonths(1);
        var label = from.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"));
        return (year, month, from, toExclusive, label);
    }

    public static (int Year, int Month, DateOnly From, DateOnly ToExclusive, string Label) Current(
        DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        return Of(today.Year, today.Month);
    }

    public static bool TryParse(int? year, int? month,
        out (int Year, int Month, DateOnly From, DateOnly ToExclusive, string Label) current,
        out string? error)
    {
        error = null;
        if (year is null && month is null)
        {
            current = Current();
            return true;
        }

        if (year is null or < 2000 or > 2100)
        {
            current = default;
            error = "Invalid or missing year";
            return false;
        }

        if (month is null or < 1 or > 12)
        {
            current = default;
            error = "Invalid or missing month (1–12)";
            return false;
        }

        current = Of(year.Value, month.Value);
        return true;
    }
}
