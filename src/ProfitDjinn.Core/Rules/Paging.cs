namespace ProfitDjinn.Core.Rules;

/// <summary>One page of a list, and where it sits in the whole.</summary>
public sealed record PageSlice<T>(IReadOnlyList<T> Items, int Page, int PageCount, int Total, int PageSize)
{
    /// <summary>1-based position of the first row shown; 0 when the list is empty.</summary>
    public int First => Total == 0 ? 0 : (Page - 1) * PageSize + 1;

    /// <summary>1-based position of the last row shown; 0 when the list is empty.</summary>
    public int Last => Total == 0 ? 0 : First + Items.Count - 1;

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;
}

/// <summary>
/// 2.8. Long lists (Invoices, Customers) are shown a page at a time. A WPF table row costs about
/// 15 ms to build, so 1,600 invoices took half a minute to open; 50 rows take well under a second.
/// </summary>
public static class Paging
{
    public const int PageSize = 50;

    /// <summary>The requested page, clamped to the pages that exist (a page past the end shows the last one).</summary>
    public static PageSlice<T> Slice<T>(IReadOnlyList<T> all, int page, int pageSize = PageSize)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "A page needs at least one row.");
        int pageCount = Math.Max(1, (all.Count + pageSize - 1) / pageSize);
        int p = Math.Clamp(page, 1, pageCount);
        var items = all.Skip((p - 1) * pageSize).Take(pageSize).ToList();
        return new PageSlice<T>(items, p, pageCount, all.Count, pageSize);
    }

    /// <summary>
    /// The page numbers to offer as buttons: always the first and last, and two either side of the
    /// current one. A null is a gap ("…"). For 33 pages on page 10: 1 … 8 9 10 11 12 … 33.
    /// </summary>
    public static IReadOnlyList<int?> Buttons(int page, int pageCount)
    {
        var shown = new SortedSet<int> { 1, pageCount };
        for (int p = page - 2; p <= page + 2; p++)
            if (p >= 1 && p <= pageCount) shown.Add(p);
        var result = new List<int?>();
        int previous = 0;
        foreach (int p in shown)
        {
            // A gap of exactly one page shows that page instead of "…".
            if (p - previous == 2) result.Add(previous + 1);
            else if (p - previous > 2) result.Add(null);
            result.Add(p);
            previous = p;
        }
        return result;
    }
}
