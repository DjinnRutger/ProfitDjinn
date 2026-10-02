using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Tests;

/// <summary>The number rules every total depends on. Expected values are what Python 3.13 and JavaScript print.</summary>
public class PyMathTests
{
    [Theory]
    [InlineData(2.675, 2, 2.67)]        // 2.675 is really 2.67499999...
    [InlineData(0.125, 2, 0.12)]        // exact tie: half to even
    [InlineData(0.375, 2, 0.38)]        // exact tie: half to even
    [InlineData(2.5, 0, 2.0)]
    [InlineData(3.5, 0, 4.0)]
    [InlineData(1.005, 2, 1.0)]
    [InlineData(99.9999, 2, 100.0)]
    [InlineData(-0.125, 2, -0.12)]
    [InlineData(1234.5678, 1, 1234.6)]
    public void Round_matches_python(double x, int digits, double expected) =>
        Assert.Equal(expected, PyMath.Round(x, digits));

    [Theory]
    [InlineData(0.125, "0.13")]          // JavaScript: exact tie goes up
    [InlineData(2.675, "2.67")]
    [InlineData(33.333333333333336, "33.33")]
    [InlineData(1.005, "1.00")]
    [InlineData(0, "0.00")]
    [InlineData(85.75, "85.75")]
    public void ToFixed_matches_javascript(double x, string expected) =>
        Assert.Equal(expected, PyMath.JsToFixedText(x, 2));

    [Fact]
    public void Sum_uses_pythons_compensated_addition()
    {
        Assert.Equal(1.0, PyMath.Sum(Enumerable.Repeat(0.1, 10)));
        Assert.Equal(0.9999999999999999, PyMath.JsSum(Enumerable.Repeat(0.1, 10)));
        Assert.Equal(0.9, PyMath.Sum(new[] { 0.1, 0.2, 0.3, 0.30000000000000004 }));
        Assert.Equal(0, PyMath.Sum(Array.Empty<double>()));
    }

    [Theory]
    [InlineData(2.5, "2.5")]
    [InlineData(3.0, "3")]
    [InlineData(0.75, "0.75")]
    [InlineData(1234567.0, "1.23457e+06")]
    [InlineData(100000.0, "100000")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(1.0 / 3, "0.333333")]
    public void G_matches_python(double x, string expected) => Assert.Equal(expected, PyMath.G(x));

    [Theory]
    [InlineData(1234.5, "$1,234.50")]
    [InlineData(0.005, "$0.01")]
    [InlineData(-5, "$-5.00")]
    public void Dollars_matches_python_format(double x, string expected) => Assert.Equal(expected, PyMath.Dollars(x));

    [Theory]
    [InlineData("3", 3)]
    [InlineData("  2.5h", 2.5)]
    [InlineData("1e2", 100)]
    [InlineData(".5", 0.5)]
    [InlineData("abc", double.NaN)]
    [InlineData("", double.NaN)]
    [InlineData("-", double.NaN)]
    public void ParseFloat_matches_javascript(string text, double expected) =>
        Assert.Equal(expected, InvoiceRows.JsParseFloat(text));

    [Fact]
    public void Line_builder_treats_blank_or_zero_quantity_as_one()
    {
        Assert.Equal(1, InvoiceRows.Quantity(""));
        Assert.Equal(1, InvoiceRows.Quantity("0"));
        Assert.Equal(0, InvoiceRows.Price(""));
        var draft = InvoiceRows.ToDraft(new InvoiceRowInput("  Setup ", "3", "33.33"));
        Assert.Equal("Setup", draft.Description);
        Assert.Equal(3 * 33.33, draft.Amount);
    }
}
