using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Infrastructure;

namespace ProfitDjinn.App.Controls;

/// <summary>One table column: header text, width, alignment and how to draw a cell.</summary>
public sealed record Column<T>(string Header, GridLength Width, Func<T, UIElement> Cell, HorizontalAlignment Align = HorizontalAlignment.Left);

/// <summary>
/// A table drawn like 1.x's Bootstrap tables (custom.css .table): upper-case 11.2px muted
/// header on the card-header colour, 12x16 cell padding, a bottom rule per row, a hover
/// tint, no zebra striping. Auto-width columns line up across rows (shared size groups).
/// </summary>
public static class Table
{
    public static FrameworkElement Build<T>(
        IReadOnlyList<Column<T>> columns,
        IEnumerable<T> rows,
        Action<T>? onRowClick = null,
        Func<T, string?>? rowBrush = null,
        IReadOnlyList<UIElement?[]>? footer = null,
        bool header = true,
        Func<int, bool>? footerShaded = null)
    {
        var scope = new Grid();
        Grid.SetIsSharedSizeScope(scope, true);
        var stack = new StackPanel();
        scope.Children.Add(stack);

        if (header)
        {
            var cells = columns.Select(c => (UIElement)new TextBlock
            {
                Text = c.Header.ToUpperInvariant(), Style = Ui.Style("TableHeaderText"), HorizontalAlignment = c.Align,
                TextWrapping = TextWrapping.NoWrap,
            }).ToArray();
            var h = RowGrid(columns, cells);
            var hb = new Border { Child = h, BorderThickness = new Thickness(0, 0, 0, 1) }
                .WithResource(Border.BackgroundProperty, "CardHeaderBg").WithResource(Border.BorderBrushProperty, "Border");
            stack.Children.Add(hb);
        }

        foreach (var item in rows)
        {
            var cells = columns.Select(c =>
            {
                var cell = c.Cell(item);
                if (cell is FrameworkElement fe && fe.HorizontalAlignment == HorizontalAlignment.Stretch) fe.HorizontalAlignment = c.Align;
                if (cell is FrameworkElement fe2) fe2.VerticalAlignment = VerticalAlignment.Center;
                return cell;
            }).ToArray();
            var grid = RowGrid(columns, cells);
            var border = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1), Background = System.Windows.Media.Brushes.Transparent }
                .WithResource(Border.BorderBrushProperty, "Border");
            string? brush = rowBrush?.Invoke(item);
            if (brush is not null) border.SetResourceReference(Border.BackgroundProperty, brush);
            border.MouseEnter += (_, _) => { if (brush is null) border.SetResourceReference(Border.BackgroundProperty, "RowHover"); };
            border.MouseLeave += (_, _) => { if (brush is null) border.Background = System.Windows.Media.Brushes.Transparent; };
            if (onRowClick is not null)
            {
                border.Cursor = Cursors.Hand;
                border.MouseLeftButtonUp += (s, e) =>
                {
                    // Buttons and links inside the row handle their own clicks.
                    if (e.OriginalSource is DependencyObject d && IsInsideButton(d, border)) return;
                    onRowClick(item);
                };
            }
            stack.Children.Add(border);
        }

        if (footer is not null)
            for (int f = 0; f < footer.Count; f++)
            {
                var grid = RowGrid(columns, footer[f].Select(c => c ?? new TextBlock()).ToArray());
                var row = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) }.WithResource(Border.BorderBrushProperty, "Border");
                // Bootstrap's table-active shading; by default every footer row has it.
                if (footerShaded?.Invoke(f) ?? true) row.SetResourceReference(Border.BackgroundProperty, "TotalsRow");
                stack.Children.Add(row);
            }
        return scope;
    }

    private static Grid RowGrid<T>(IReadOnlyList<Column<T>> columns, UIElement[] cells)
    {
        var g = new Grid();
        for (int i = 0; i < columns.Count; i++)
        {
            var cd = new ColumnDefinition { Width = columns[i].Width };
            if (columns[i].Width.IsAuto) cd.SharedSizeGroup = "c" + i;
            g.ColumnDefinitions.Add(cd);
            if (i >= cells.Length) continue;
            var host = new Border { Padding = new Thickness(16, 12, 16, 12), Child = cells[i] };
            Grid.SetColumn(host, i);
            g.Children.Add(host);
        }
        return g;
    }

    private static bool IsInsideButton(DependencyObject d, DependencyObject stop)
    {
        for (var cur = d; cur is not null && cur != stop; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
            if (cur is Button or Link or CheckBox or TextBox) return true;
        return false;
    }
}
