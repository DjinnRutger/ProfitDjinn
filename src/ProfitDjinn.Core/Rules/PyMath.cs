using System.Globalization;
using System.Numerics;

namespace ProfitDjinn.Core.Rules;

/// <summary>
/// Rounding and number formatting that match the 1.x app exactly, so the same database shows
/// the same cents in both builds.
///
/// Python's round() and JavaScript's toFixed() both round the double's exact binary value,
/// and they break ties differently (Python half-to-even, JavaScript half-up). Math.Round
/// scales by a power of ten first, which can land on the other side of a tie. So these work
/// on the exact value with BigInteger arithmetic.
/// </summary>
public static class PyMath
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Python 3 round(x, digits).</summary>
    public static double Round(double x, int digits = 0) => RoundExact(x, digits, tiesToEven: true);

    /// <summary>The number JavaScript's x.toFixed(digits) prints, as a double.</summary>
    public static double JsToFixed(double x, int digits) => RoundExact(x, digits, tiesToEven: false);

    /// <summary>JavaScript's x.toFixed(digits) as text, e.g. "33.33".</summary>
    public static string JsToFixedText(double x, int digits) =>
        JsToFixed(x, digits).ToString("F" + digits, Inv);

    private static double RoundExact(double x, int digits, bool tiesToEven)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        bool negative = x < 0;
        double ax = Math.Abs(x);

        // ax = mantissa * 2^exp, exactly.
        long bits = BitConverter.DoubleToInt64Bits(ax);
        int rawExp = (int)((bits >> 52) & 0x7FF);
        long frac = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = rawExp == 0 ? frac : frac | (1L << 52);
        int exp = (rawExp == 0 ? 1 : rawExp) - 1075;

        // ax * 10^digits = num / den, exactly.
        BigInteger num = mantissa * BigInteger.Pow(10, Math.Max(digits, 0));
        BigInteger den = BigInteger.Pow(10, Math.Max(-digits, 0));
        if (exp >= 0) num <<= exp; else den <<= -exp;

        BigInteger q = BigInteger.DivRem(num, den, out BigInteger rem);
        int cmp = (rem * 2).CompareTo(den);
        // Ties: Python goes to even. JavaScript goes to the larger magnitude here, because
        // toFixed of a negative number is minus toFixed of its absolute value.
        if (cmp > 0 || (cmp == 0 && (!tiesToEven || !q.IsEven))) q += 1;

        string text = digits > 0
            ? InsertPoint(q.ToString(Inv), digits)
            : (q * BigInteger.Pow(10, -digits)).ToString(Inv);
        double result = double.Parse(text, Inv);
        return negative ? -result : result;
    }

    private static string InsertPoint(string digitsText, int places)
    {
        string padded = digitsText.PadLeft(places + 1, '0');
        return padded[..^places] + "." + padded[^places..];
    }

    /// <summary>
    /// Python 3.12+ sum() over floats, which 1.x used for every total. It is not plain
    /// left-to-right addition: it carries a Neumaier compensation term, so sum([0.1]*10) is
    /// exactly 1.0. Plain addition gives 0.9999999999999999, which can move a cent.
    /// An empty sequence gives 0.
    /// </summary>
    public static double Sum(IEnumerable<double> values)
    {
        using var e = values.GetEnumerator();
        if (!e.MoveNext()) return 0;
        double total = 0 + e.Current;   // Python: int 0 + first float
        double c = 0;
        while (e.MoveNext())
        {
            double x = e.Current;
            double t = total + x;
            if (Math.Abs(total) >= Math.Abs(x)) c += (total - t) + x;
            else c += (x - t) + total;
            total = t;
        }
        if (c != 0 && double.IsFinite(c)) total += c;
        return total;
    }

    public static double Sum<T>(IEnumerable<T> items, Func<T, double> selector) => Sum(items.Select(selector));

    /// <summary>JavaScript's arr.reduce((s, x) => s + x, 0): plain left-to-right addition.</summary>
    public static double JsSum(IEnumerable<double> values)
    {
        double total = 0;
        foreach (double x in values) total += x;
        return total;
    }

    /// <summary>Python's f"{x:g}": 6 significant digits, no trailing zeros.</summary>
    public static string G(double x)
    {
        if (x == 0) return "0";
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";

        // The exponent after rounding to 6 significant digits decides the notation.
        string sci = x.ToString("E5", Inv);                  // d.dddddE+xxx
        int ePos = sci.IndexOf('E');
        int e = int.Parse(sci[(ePos + 1)..], Inv);
        if (e < -4 || e >= 6)
        {
            string mant = sci[..ePos];
            if (mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            return $"{mant}e{(e < 0 ? "-" : "+")}{Math.Abs(e):00}";
        }
        int decimals = Math.Max(0, 5 - e);
        string s = Round(x, decimals).ToString("F" + decimals, Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s;
    }

    /// <summary>Python's repr(x) / str(x) of a float, as a 1.x template printed a raw number: "2.5", "0.1", "1e-05".</summary>
    public static string Repr(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        if (x == Math.Floor(x) && Math.Abs(x) < 1e16) return ((decimal)x).ToString(Inv) + ".0";
        string r = x.ToString("R", Inv);
        int e = r.IndexOfAny(new[] { 'E', 'e' });
        if (e < 0) return r;
        int exp = int.Parse(r[(e + 1)..], Inv);
        return $"{r[..e]}e{(exp < 0 ? "-" : "+")}{Math.Abs(exp):00}";
    }

    /// <summary>Python's f"{x:,.2f}": thousands separators, two decimals.</summary>
    public static string Money(double x) => Round(x, 2).ToString("#,##0.00", Inv);

    /// <summary>"$1,234.50", the way the screens and the PDF show money.</summary>
    public static string Dollars(double x) => "$" + Money(x);
}
