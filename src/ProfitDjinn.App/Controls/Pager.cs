using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// 2.8. The bar under a paged table: "Showing 51-100 of 1,604 invoices" and Previous / page
/// numbers / Next (Bootstrap pagination). Each button names its page for UI Automation.
/// </summary>
public static class Pager
{
    /// <summary>Null when everything fits on one page, so short lists look as they always did.</summary>
    public static FrameworkElement? Build<T>(PageSlice<T> slice, string noun, Action<int> go)
    {
        if (slice.PageCount <= 1) return null;

        var buttons = Ui.Row(4);
        buttons.Children.Add(Nav("Previous", "chevron-left", slice.HasPrevious, () => go(slice.Page - 1)));
        foreach (int? p in Paging.Buttons(slice.Page, slice.PageCount))
        {
            if (p is not { } page)
            {
                buttons.Children.Add(Ui.Muted("…", 13.6).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
                continue;
            }
            var b = Ui.Button(page.ToString("N0"), page == slice.Page ? "Btn.Primary" : "Btn.OutlineSecondary", null, () => go(page), small: true);
            AutomationProperties.SetName(b, $"Page {page}");
            if (page == slice.Page) b.IsHitTestVisible = false;
            buttons.Children.Add(b);
        }
        buttons.Children.Add(Nav("Next", "chevron-right", slice.HasNext, () => go(slice.Page + 1)));

        var showing = Ui.Muted($"Showing {slice.First:N0}–{slice.Last:N0} of {slice.Total:N0} {noun}", 13.6);
        showing.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(showing, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(showing);
        bar.Children.Add(buttons);
        return new Border { Child = bar, Padding = new Thickness(16, 12, 16, 12) };
    }

    private static Button Nav(string text, string glyph, bool enabled, Action go)
    {
        var b = Ui.Button(text, "Btn.OutlineSecondary", glyph, go, small: true);
        AutomationProperties.SetName(b, text + " page");
        b.IsEnabled = enabled;
        return b;
    }
}
