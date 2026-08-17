using System.Globalization;

namespace H1Stats.Core.Services;

/// <summary>Calendar-month helpers for rolling 12-month referrer history.</summary>
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
}
