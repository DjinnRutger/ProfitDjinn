using System.Globalization;

namespace ProfitDjinn.Core.Rules;

/// <summary>
/// Next invoice or work order number: prefix + (highest existing number + 1), padded to 4
/// digits. The "next number" setting only seeds an empty table and is never written back.
///
/// Fixed in 2.0: 1.x took the alphabetically highest number, so "JQ999" beat "JQ1000" and the
/// app suggested a number that already existed. This takes the highest by value.
/// </summary>
public static class Numbering
{
    public static string Next(string prefix, IEnumerable<string> existing, string seedSetting)
    {
        prefix ??= "";
        long? highest = null;
        foreach (string number in existing)
        {
            // SQLite LIKE 'prefix%' is case-insensitive for ASCII, as 1.x's query was.
            if (!number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (long.TryParse(number[prefix.Length..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
                highest = highest is null ? n : Math.Max(highest.Value, n);
        }
        long next = highest is { } h ? h + 1 : Seed(seedSetting);
        return prefix + next.ToString("D4", CultureInfo.InvariantCulture);
    }

    private static long Seed(string setting)
    {
        if (long.TryParse(setting?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) return n;
        if (double.TryParse(setting?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return (long)d;
        return 1001;
    }
}
