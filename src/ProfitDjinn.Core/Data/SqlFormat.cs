using System.Globalization;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// The text formats SQLAlchemy uses in this database. Writing anything else would make the
/// file unreadable to the 1.x build, which is the rollback path.
///   Date      2026-10-02
///   DateTime  2026-10-02 14:03:09.123456   (naive, UTC)
/// </summary>
public static class SqlFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    public static string? Date(DateOnly? d) => d is { } v ? Date(v) : null;

    public static string DateTime(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss.ffffff", Inv);

    public static string NowUtc() => DateTime(System.DateTime.UtcNow);

    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // A DATE column holds exactly YYYY-MM-DD, but be lenient with anything longer.
        return DateOnly.ParseExact(s.Length >= 10 ? s[..10] : s, "yyyy-MM-dd", Inv);
    }

    public static DateTime? ParseDateTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return System.DateTime.Parse(s, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
