using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ProfitDjinn.App.Infrastructure;

namespace ProfitDjinn.App.Controls;

/// <summary>One choice in a <see cref="RecordPicker"/>: a customer or vendor id, its name, and a muted detail (Attn, contact).</summary>
public sealed record PickItem(long Id, string Name, string? Detail = null);

/// <summary>
/// 2.6. Type-to-pick for a customer or vendor, in place of a long dropdown.
/// - Typing lists the names that contain the text (names that start with it first) and fills in
///   the rest of the best match, selected, so Tab or Enter accepts it. Deleting never fills in.
/// - A name typed exactly (any case) picks that record; any other text picks nothing.
/// - The arrow button, or focusing the empty box, lists everything, like the old dropdown.
/// - With text that matches no one, an "Add Customer 'X'" button appears under the box and raises
///   <see cref="AddRequested"/>; the page creates the record and calls <see cref="Add"/>.
/// The inner TextBox carries the field's label as its accessible name (see Field).
/// </summary>
public sealed class RecordPicker : StackPanel
{
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), MaxHeight = 8 * 34 };
    private readonly Button _add;
    private readonly string _addLabel;
    private List<PickItem> _items = new();
    private bool _typed, _quiet;
    private long? _selected;

    public TextBox Box { get; }

    /// <summary>Raised when the picked record changes (including to none).</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised by the Add button with the typed name.</summary>
    public event Action<string>? AddRequested;

    public long? SelectedId => _selected;
    public string Text => Box.Text.Trim();

    /// <summary>True when something is typed that is not one of the records.</summary>
    public bool HasUnmatchedText => Text.Length > 0 && _selected is null;

    public bool Invalid { get => Input.GetInvalid(Box); set => Input.SetInvalid(Box, value); }

    /// <param name="addLabel">"Add Customer" or "Add Vendor": the button's text and accessible name.</param>
    public RecordPicker(IEnumerable<PickItem> items, long? selected, string placeholder, string addLabel)
    {
        _addLabel = addLabel;
        _items = items.ToList();

        Box = Ui.TextBox(null, placeholder);
        Box.Padding = new Thickness(11, 6, 34, 6);   // the input style's 11,6, plus room for the arrow
        var arrow = new Button { Style = Ui.Style("Btn.Bare"), Focusable = false, Padding = new Thickness(8, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Show all" }
            .WithResource(Button.ForegroundProperty, "TextMuted");
        Btn.SetIcon(arrow, "chevron-down");
        Btn.SetSmall(arrow, true);
        System.Windows.Automation.AutomationProperties.SetName(arrow, "Show all");
        arrow.Click += (_, _) =>
        {
            if (_popup.IsOpen) { _popup.IsOpen = false; return; }
            Box.Focus();
            Show(all: true);
        };
        var boxRow = new Grid();
        boxRow.Children.Add(Box);
        boxRow.Children.Add(arrow);
        Children.Add(boxRow);

        _add = new Button { Style = Ui.Style("Btn.OutlineSuccess"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        Btn.SetIcon(_add, "plus-lg");
        Btn.SetSmall(_add, true);
        System.Windows.Automation.AutomationProperties.SetName(_add, addLabel);
        _add.Click += (_, _) => AddRequested?.Invoke(Text);
        Children.Add(_add);

        // A few hundred names at most: build every row, so UI Automation and screen readers see them all.
        VirtualizingPanel.SetIsVirtualizing(_list, false);
        _list.SetResourceReference(Control.BackgroundProperty, "Surface");
        _list.SetResourceReference(Control.ForegroundProperty, "Text");
        _popup.PlacementTarget = Box;
        _popup.Child = new Border { BorderThickness = new Thickness(1), Child = _list, Padding = new Thickness(0, 4, 0, 4) }
            .WithResource(Border.BackgroundProperty, "Surface").WithResource(Border.BorderBrushProperty, "Border")
            .WithResource(Border.CornerRadiusProperty, "RadiusSmall");
        Children.Add(_popup);

        Box.PreviewTextInput += (_, _) => _typed = true;
        Box.TextChanged += (_, _) => OnTextChanged();
        Box.PreviewKeyDown += OnKey;
        Box.GotKeyboardFocus += (_, _) => { if (Box.Text.Length == 0) Show(all: true); };
        Box.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is DependencyObject d && IsInList(d)) return;
            _popup.IsOpen = false;
            if (_selected is { } id && _items.FirstOrDefault(i => i.Id == id) is { } item && Box.Text != item.Name) SetText(item.Name);
        };
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_list, d) is ListBoxItem { Tag: PickItem item }) Pick(item);
        };
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (_list.SelectedItem as ListBoxItem)?.Tag is PickItem item) { Pick(item); e.Handled = true; }
            else if (e.Key == Key.Escape) { _popup.IsOpen = false; Box.Focus(); e.Handled = true; }
            else if (e.Key == Key.Up && _list.SelectedIndex <= 0) { Box.Focus(); e.Handled = true; }
        };

        Select(selected);
    }

    /// <summary>Picks a record by id (or none), filling in its name.</summary>
    public void Select(long? id)
    {
        var item = id is { } v ? _items.FirstOrDefault(i => i.Id == v) : null;
        SetText(item?.Name ?? "");
        SetSelected(item?.Id);
    }

    /// <summary>Adds a record (a quick-added customer or vendor) and, by default, picks it.</summary>
    public void Add(PickItem item, bool select = true)
    {
        _items.RemoveAll(i => i.Id == item.Id);
        _items.Add(item);
        _items = _items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (select) Select(item.Id);
    }

    public void FocusBox() => Dispatcher.BeginInvoke(() => { Box.Focus(); Box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);

    // ------------------------------------------------------------------ typing

    private void OnTextChanged()
    {
        if (_quiet) return;
        bool typed = _typed;
        _typed = false;
        string text = Box.Text;

        // Fill in the rest of the best name, selected, when the user has just typed a character
        // at the end of the box.
        if (typed && text.Length > 0 && Box.CaretIndex == text.Length && Box.SelectionLength == 0)
        {
            var best = Matches(text).FirstOrDefault(i => i.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) && i.Name.Length > text.Length);
            if (best is not null)
            {
                SetText(text + best.Name[text.Length..]);
                Box.Select(text.Length, best.Name.Length - text.Length);
            }
        }
        var exact = _items.FirstOrDefault(i => string.Equals(i.Name, Box.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        SetSelected(exact?.Id);
        if (Box.IsKeyboardFocused) Show(all: Box.Text.Length == 0);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back or Key.Delete:
                _typed = false;        // deleting never fills a name back in
                break;
            case Key.Down when _popup.IsOpen && _list.Items.Count > 0:
                _list.SelectedIndex = 0;
                ((ListBoxItem)_list.Items[0]).Focus();
                e.Handled = true;
                break;
            case Key.Down:
                Show(all: true);
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen || Box.SelectionLength > 0:
                if (Box.SelectionLength > 0 && Box.SelectionStart + Box.SelectionLength == Box.Text.Length)
                    SetTextAndMatch(Box.Text[..Box.SelectionStart]);       // drop the filled-in part
                _popup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Enter when Box.SelectionLength > 0 || _popup.IsOpen:
                Accept();
                e.Handled = true;      // the first Enter accepts the name; it does not save the form
                break;
            case Key.Tab:
                Accept();
                break;
        }
    }

    /// <summary>Takes the filled-in name: caret to the end, list closed.</summary>
    private void Accept()
    {
        Box.Select(Box.Text.Length, 0);
        _popup.IsOpen = false;
        if (_selected is { } id && _items.FirstOrDefault(i => i.Id == id) is { } item && Box.Text != item.Name) SetText(item.Name);
    }

    private void Pick(PickItem item)
    {
        SetText(item.Name);
        SetSelected(item.Id);
        _popup.IsOpen = false;
        Box.Focus();
        Box.Select(Box.Text.Length, 0);
    }

    private void SetTextAndMatch(string text)
    {
        SetText(text);
        Box.Select(text.Length, 0);
        SetSelected(_items.FirstOrDefault(i => string.Equals(i.Name, text.Trim(), StringComparison.OrdinalIgnoreCase))?.Id);
    }

    private void SetText(string text)
    {
        _quiet = true;
        Box.Text = text;
        _quiet = false;
    }

    private void SetSelected(long? id)
    {
        bool changed = id != _selected;
        _selected = id;
        string text = Text;
        _add.Visibility = text.Length > 0 && id is null ? Visibility.Visible : Visibility.Collapsed;
        _add.Content = $"{_addLabel} “{(text.Length > 40 ? text[..40] + "…" : text)}”";
        if (id is not null) Invalid = false;
        if (changed) SelectionChanged?.Invoke();
    }

    // ------------------------------------------------------------------ the list

    /// <summary>Names containing the text; those starting with it first, then by name.</summary>
    private IEnumerable<PickItem> Matches(string text)
    {
        string t = text.Trim();
        return _items
            .Where(i => t.Length == 0 || i.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || (i.Detail ?? "").Contains(t, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name.StartsWith(t, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void Show(bool all)
    {
        // While a name is filled in, list against what was actually typed.
        string typed = Box.SelectionLength > 0 && Box.SelectionStart + Box.SelectionLength == Box.Text.Length ? Box.Text[..Box.SelectionStart] : Box.Text;
        var matches = (all ? Matches("") : Matches(typed)).ToList();
        _list.Items.Clear();
        foreach (var item in matches)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = item.Name, FontSize = 14 }.WithResource(TextBlock.ForegroundProperty, "Text"));
            if (!string.IsNullOrEmpty(item.Detail))
                row.Children.Add(new TextBlock { Text = "  " + item.Detail, FontSize = 12.8 }.WithResource(TextBlock.ForegroundProperty, "TextMuted"));
            _list.Items.Add(new ListBoxItem { Content = row, Tag = item, Padding = new Thickness(10, 6, 10, 6) });
        }
        _popup.MinWidth = Box.ActualWidth;
        _popup.IsOpen = matches.Count > 0 && Box.IsKeyboardFocused;
    }

    private bool IsInList(DependencyObject d)
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            if (ReferenceEquals(x, _list)) return true;
        return false;
    }
}
