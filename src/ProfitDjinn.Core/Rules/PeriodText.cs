using System.Globalization;
using System.Text.RegularExpressions;

namespace ProfitDjinn.Core.Rules;

/// <summary>
/// 2.4. The billing-period placeholders on a recurring invoice: "{month}" becomes the invoice
/// date's month name ("November") and "{year}" its year ("2026"). Any case; other braces are
/// left as typed.
/// </summary>
public static partial class PeriodText
{
    public static string Fill(string? text, DateOnly date)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return Placeholder().Replace(text, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "month" => date.ToString("MMMM", CultureInfo.InvariantCulture),
            _ => date.Year.ToString(CultureInfo.InvariantCulture),
        });
    }

    [GeneratedRegex(@"\{(month|year)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
