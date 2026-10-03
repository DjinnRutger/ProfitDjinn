namespace ProfitDjinn.Core.Rules;

/// <summary>
/// A repeating date: every <see cref="MonthsPerStep"/> months on <see cref="DayOfMonth"/>,
/// from <see cref="Start"/> until <see cref="End"/> (inclusive, optional). Shared by recurring
/// expenses (2.2) and recurring invoices (2.4).
///
/// Every date is worked out from the start month, never from the previous date, so day 31
/// gives Jan 31, Feb 28 (29 in a leap year), Mar 31; a yearly Feb 29 gives Feb 28 in other years.
/// </summary>
public sealed record Schedule(DateOnly Start, int MonthsPerStep, int DayOfMonth, DateOnly? End)
{
    /// <summary>The dates between <paramref name="from"/> and <paramref name="to"/>, inclusive.</summary>
    public IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly to)
    {
        if (MonthsPerStep < 1) throw new ArgumentOutOfRangeException(nameof(MonthsPerStep));
        var anchor = new DateOnly(Start.Year, Start.Month, 1);
        for (int k = 0; ; k++)
        {
            var month = anchor.AddMonths(k * MonthsPerStep);
            var d = new DateOnly(month.Year, month.Month, Math.Min(DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
            if (d > to) yield break;
            if (End is { } end && d > end) yield break;
            if (d < Start || d < from) continue;
            yield return d;
        }
    }

    /// <summary>The first date on or after <paramref name="from"/>, or null once the schedule has ended.</summary>
    public DateOnly? FirstFrom(DateOnly from)
    {
        foreach (var d in Occurrences(from, DateOnly.MaxValue.AddYears(-1))) return d;
        return null;
    }
}
