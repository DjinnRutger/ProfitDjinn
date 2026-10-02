using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ProfitDjinn.App.Infrastructure;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// A text box with a list of suggestions under it, like an HTML &lt;input list=...&gt; with a
/// &lt;datalist&gt; (1.x's Project field). Typing filters the list; any text is allowed.
/// </summary>
public sealed class SuggestBox : Grid
{
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), MaxHeight = 220 };
    private readonly IReadOnlyList<string> _all;

    public TextBox Box { get; }

    public string Text { get => Box.Text; set => Box.Text = value; }

    public SuggestBox(IReadOnlyList<string> suggestions, string? text, string? placeholder, bool small = false)
    {
        _all = suggestions;
        Box = Ui.TextBox(text, placeholder);
        if (small) Box.Style = Ui.Style("Input.Small");
        Children.Add(Box);
        _list.SetResourceReference(Control.BackgroundProperty, "Surface");
        _list.SetResourceReference(Control.ForegroundProperty, "Text");
        _popup.PlacementTarget = Box;
        _popup.Child = new Border { BorderThickness = new Thickness(1), Child = _list, Padding = new Thickness(0, 4, 0, 4) }
            .WithResource(Border.BackgroundProperty, "Surface").WithResource(Border.BorderBrushProperty, "Border")
            .WithResource(Border.CornerRadiusProperty, "RadiusSmall");
        Children.Add(_popup);

        Box.GotKeyboardFocus += (_, _) => Show();
        Box.TextChanged += (_, _) => { if (Box.IsKeyboardFocused) Show(); };
        Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _popup.IsOpen && _list.Items.Count > 0) { _list.SelectedIndex = 0; ((ListBoxItem)_list.ItemContainerGenerator.ContainerFromIndex(0))?.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) _popup.IsOpen = false;
        };
        _list.PreviewMouseLeftButtonUp += (_, _) => Pick();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Pick(); e.Handled = true; } };
    }

    private void Show()
    {
        string typed = Box.Text.Trim();
        var matches = _all.Where(s => s.Contains(typed, StringComparison.OrdinalIgnoreCase) && s != typed).ToList();
        _list.ItemsSource = matches;
        _popup.MinWidth = Box.ActualWidth;
        _popup.IsOpen = matches.Count > 0;
    }

    private void Pick()
    {
        if (_list.SelectedItem is string s)
        {
            Box.Text = s;
            Box.CaretIndex = s.Length;
        }
        _popup.IsOpen = false;
        Box.Focus();
    }
}
