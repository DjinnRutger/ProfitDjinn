using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Tests;

/// <summary>2.8. Paging of the Invoices and Customers lists.</summary>
public class PagingTests
{
    private static readonly int[] Rows = Enumerable.Range(1, 120).ToArray();

    [Fact]
    public void First_page_holds_the_first_rows()
    {
        var s = Paging.Slice(Rows, 1);
        Assert.Equal(Enumerable.Range(1, 50), s.Items);
        Assert.Equal((1, 3, 120, 1, 50), (s.Page, s.PageCount, s.Total, s.First, s.Last));
        Assert.False(s.HasPrevious);
        Assert.True(s.HasNext);
    }

    [Fact]
    public void Last_page_holds_the_remainder()
    {
        var s = Paging.Slice(Rows, 3);
        Assert.Equal(Enumerable.Range(101, 20), s.Items);
        Assert.Equal((101, 120), (s.First, s.Last));
        Assert.True(s.HasPrevious);
        Assert.False(s.HasNext);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(4, 3)]
    [InlineData(999, 3)]
    public void Out_of_range_pages_are_clamped(int asked, int shown) =>
        Assert.Equal(shown, Paging.Slice(Rows, asked).Page);

    [Fact]
    public void Empty_list_is_one_empty_page()
    {
        var s = Paging.Slice(Array.Empty<int>(), 1);
        Assert.Empty(s.Items);
        Assert.Equal((1, 1, 0, 0, 0), (s.Page, s.PageCount, s.Total, s.First, s.Last));
        Assert.False(s.HasNext);
    }

    [Fact]
    public void Exactly_one_full_page_has_no_second_page() =>
        Assert.Equal(1, Paging.Slice(Enumerable.Range(1, 50).ToArray(), 1).PageCount);

    [Theory]
    [InlineData(1, 1, "1")]
    [InlineData(1, 5, "1 2 3 4 5")]
    [InlineData(1, 33, "1 2 3 … 33")]
    [InlineData(10, 33, "1 … 8 9 10 11 12 … 33")]
    [InlineData(4, 33, "1 2 3 4 5 6 … 33")]
    [InlineData(33, 33, "1 … 31 32 33")]
    public void Buttons_show_ends_and_neighbours(int page, int count, string expected) =>
        Assert.Equal(expected, string.Join(" ", Paging.Buttons(page, count).Select(p => p?.ToString() ?? "…")));
}
