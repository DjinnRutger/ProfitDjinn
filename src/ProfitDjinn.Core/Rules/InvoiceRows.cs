using System.Globalization;

namespace ProfitDjinn.Core.Rules;

/// <summary>
/// One editable row of the invoice line builder: description, quantity and unit price as the
/// user typed them. Shared by the invoice form and the bill screen.
/// </summary>
/// <remarks>
/// 2.5: optional service dates (invoice lines), or <paramref name="BillPeriod"/> on a recurring
/// invoice line, which fills the service dates with the period each invoice covers.
/// </remarks>
public sealed record InvoiceRowInput(string Description, string Quantity, string UnitPrice,
    DateOnly? ServiceStart = null, DateOnly? ServiceEnd = null, bool BillPeriod = false);

/// <summary>An invoice line ready to save: quantity and extended amount (plus the 2.5 service dates).</summary>
public sealed record InvoiceLineDraft(string Description, double Quantity, double Amount,
    DateOnly? ServiceStart = null, DateOnly? ServiceEnd = null, bool BillPeriod = false);

/// <summary>
/// How 1.x's JavaScript turned the line builder's text boxes into numbers, because the
/// saved amounts depend on it:
///   qty    = parseFloat(text), or 1 when blank or unreadable
///   price  = parseFloat(text) || 0
///   amount = qty * price               (the extended total; not rounded on the invoice form)
/// Fixed in 2.0: 1.x wrote parseFloat(text) || 1, so a quantity of 0 became 1. On the bill
/// screen that billed a labor line logged at 0 hours as a full hour. A 0 now stays 0.
/// </summary>
public static class InvoiceRows
{
    public static double Quantity(string? text)
    {
        double q = JsParseFloat(text);
        return double.IsNaN(q) ? 1 : q;
    }

    public static double Price(string? text)
    {
        double p = JsParseFloat(text);
        return double.IsNaN(p) ? 0 : p;
    }

    public static InvoiceLineDraft ToDraft(InvoiceRowInput row)
    {
        double qty = Quantity(row.Quantity);
        return new InvoiceLineDraft((row.Description ?? "").Trim(), qty, qty * Price(row.UnitPrice),
            row.ServiceStart, row.ServiceEnd, row.BillPeriod);
    }

    /// <summary>The live total under the line builder: sum of qty * price, plain addition.</summary>
    public static double Total(IEnumerable<InvoiceRowInput> rows) =>
        PyMath.JsSum(rows.Select(r => Quantity(r.Quantity) * Price(r.UnitPrice)));

    /// <summary>
    /// JavaScript parseFloat: reads the longest number at the start of the text, ignoring
    /// leading whitespace and anything after the number; NaN when there is none.
    /// </summary>
    public static double JsParseFloat(string? text)
    {
        if (text is null) return double.NaN;
        string s = text.TrimStart();
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        if (s.AsSpan(i).StartsWith("Infinity", StringComparison.Ordinal))
            return s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
        int digitsStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
        if (i == digitsStart || (i == digitsStart + 1 && s[digitsStart] == '.')) return double.NaN;
        int mantissaEnd = i;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            int expDigits = j;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (j > expDigits) mantissaEnd = j;
        }
        return double.Parse(s[..mantissaEnd], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>How JavaScript prints a number into a text box: shortest round-trip form.</summary>
    public static string JsNumberText(double x) =>
        x == Math.Floor(x) && Math.Abs(x) < 1e21
            ? ((decimal)x).ToString(CultureInfo.InvariantCulture)
            : x.ToString("R", CultureInfo.InvariantCulture);
}
