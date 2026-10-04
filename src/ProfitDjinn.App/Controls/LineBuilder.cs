using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// The editable invoice-lines table of 1.x's invoice form and bill screen: description,
/// quantity, unit price and a remove button per row, a Total footer, and an empty message.
/// Rows keep the text as typed; <see cref="InvoiceRows"/> turns it into numbers exactly as
/// 1.x's JavaScript did.
/// </summary>
/// <summary>2.5. What the per-line calendar button offers.</summary>
public enum LineDates
{
    /// <summary>No service dates.</summary>
    None,
    /// <summary>Optional From / To service dates (invoice form, bill screen).</summary>
    Dates,
    /// <summary>A "bill the period" tick (recurring invoices): each invoice fills in the period it covers.</summary>
    Period,
}

public sealed class LineBuilder : StackPanel
{
    private sealed class Row
    {
        public required Border Host;
        public required TextBox Description, Quantity, UnitPrice;
        public DateBox? From, To;
        public CheckBox? Period;
        public InvoiceRowInput Input => new(Description.Text, Quantity.Text, UnitPrice.Text, From?.Date, To?.Date, Period?.IsChecked == true);
    }

    private readonly List<Row> _rows = new();
    private readonly StackPanel _body = new();
    private readonly TextBlock _total = new() { FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly FrameworkElement _empty;
    private bool _suppress;
    private readonly LineDates _dates;

    /// <summary>Raised after any row is added, removed or edited.</summary>
    public event Action? Changed;

    /// <summary>True once the user has typed in, added or removed a row since the last <see cref="SetRows"/>.</summary>
    public bool IsDirty { get; private set; }

    public LineBuilder(string emptyText, LineDates dates = LineDates.None)
    {
        _dates = dates;
        Grid.SetIsSharedSizeScope(this, true);
        var header = RowGrid(
            Head("Description"), Head("Qty", HorizontalAlignment.Center), Head("Unit Price ($)", HorizontalAlignment.Right), new TextBlock());
        Children.Add(new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }
            .WithResource(Border.BackgroundProperty, "CardHeaderBg").WithResource(Border.BorderBrushProperty, "Border"));
        Children.Add(_body);
        var footer = RowGrid(new TextBlock(), Ui.Text("Total", "Strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right), _total, new TextBlock(), totalSpansQty: true);
        Children.Add(new Border { Child = footer, BorderThickness = new Thickness(0, 0, 0, 1) }
            .WithResource(Border.BackgroundProperty, "TotalsRow").WithResource(Border.BorderBrushProperty, "Border"));
        _empty = new TextBlock { Text = emptyText, TextAlignment = TextAlignment.Center, Margin = new Thickness(16), FontSize = 14.4, TextWrapping = TextWrapping.Wrap }
            .WithResource(TextBlock.ForegroundProperty, "TextMuted");
        Children.Add(_empty);
        Refresh();
    }

    public IReadOnlyList<InvoiceRowInput> Rows => _rows.Select(r => r.Input).ToList();

    public int Count => _rows.Count;

    /// <summary>Live total: sum of qty * price, plain addition, as the JavaScript added it.</summary>
    public double Total => InvoiceRows.Total(Rows);

    public void AddRow(string description, string quantity, string unitPrice, bool focus = false, bool byUser = true,
        DateOnly? serviceStart = null, DateOnly? serviceEnd = null, bool billPeriod = false)
    {
        var desc = Ui.TextBox(description, "Description").Also(t => t.Style = Ui.Style("Input.Small"));
        var qty = Ui.TextBox(quantity).Also(t => { t.Style = Ui.Style("Input.Small"); t.HorizontalContentAlignment = HorizontalAlignment.Center; t.Width = 72; });
        var unit = Ui.TextBox(unitPrice, "0.00").Also(t => { t.Style = Ui.Style("Input.Small"); t.HorizontalContentAlignment = HorizontalAlignment.Right; t.Width = 110; });
        var remove = new Button { Style = Ui.Style("Btn.Bare"), ToolTip = "Remove", Padding = new Thickness(6, 4, 6, 4) }.WithResource(Button.ForegroundProperty, "DangerText");
        Btn.SetIcon(remove, "trash3");
        Btn.SetSmall(remove, true);

        var row = new Row { Host = null!, Description = desc, Quantity = qty, UnitPrice = unit };
        UIElement actions = remove;
        FrameworkElement? extra = null;
        if (_dates != LineDates.None)
        {
            // 2.5: a calendar button opens a second line under the row for the service dates.
            var calendar = new Button { Style = Ui.Style("Btn.Bare"), Padding = new Thickness(6, 4, 6, 4),
                ToolTip = _dates == LineDates.Dates ? "Service dates" : "Bill the period" }.WithResource(Button.ForegroundProperty, "TextMuted");
            Btn.SetIcon(calendar, "calendar3");
            Btn.SetSmall(calendar, true);
            System.Windows.Automation.AutomationProperties.SetName(calendar, (string)calendar.ToolTip);
            extra = DatesLine(row, serviceStart, serviceEnd, billPeriod);
            bool has = serviceStart is not null || serviceEnd is not null || billPeriod;
            extra.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            var shownExtra = extra;
            calendar.Click += (_, _) =>
            {
                shownExtra.Visibility = shownExtra.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (shownExtra.Visibility == Visibility.Visible) (row.From?.TextBox as UIElement ?? row.Period)?.Focus();
            };
            actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { calendar, remove } };
        }

        var grid = RowGrid(desc, qty, unit, actions);
        var content = new StackPanel { Children = { grid } };
        if (extra is not null) content.Children.Add(extra);
        var host = new Border { Child = content, BorderThickness = new Thickness(0, 0, 0, 1) }.WithResource(Border.BorderBrushProperty, "Border");
        row.Host = host;
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            _body.Children.Remove(host);
            IsDirty = true;
            Refresh();
        };
        foreach (var box in new[] { desc, qty, unit })
            box.TextChanged += (_, _) => { if (_suppress) return; IsDirty = true; Refresh(); };

        _rows.Add(row);
        _body.Children.Add(host);
        if (byUser) IsDirty = true;
        Refresh();
        if (focus) Dispatcher.BeginInvoke(() => desc.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Replaces every row (a roll-up rebuild, or the saved lines on edit). Clears the dirty flag.</summary>
    public void SetRows(IEnumerable<InvoiceRowInput> rows)
    {
        _suppress = true;
        _rows.Clear();
        _body.Children.Clear();
        foreach (var r in rows) AddRow(r.Description, r.Quantity, r.UnitPrice, byUser: false,
            serviceStart: r.ServiceStart, serviceEnd: r.ServiceEnd, billPeriod: r.BillPeriod);
        _suppress = false;
        IsDirty = false;
        Refresh();
    }

    private void Refresh()
    {
        _total.Text = Ui.MoneyGrouped(Total);
        _empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_suppress) Changed?.Invoke();
    }

    /// <summary>The second line of a row: "Service [from] to [to]", or the bill-the-period tick.</summary>
    private FrameworkElement DatesLine(Row row, DateOnly? start, DateOnly? end, bool period)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 12, 10) };
        if (_dates == LineDates.Period)
        {
            row.Period = new CheckBox
            {
                IsChecked = period, VerticalAlignment = VerticalAlignment.Center,
                Content = Ui.Muted("Show the period each invoice covers as service dates (e.g. Service: 11/01/26 - 11/30/26)", 13),
            };
            System.Windows.Automation.AutomationProperties.SetName(row.Period, "Show the period as service dates");
            row.Period.Checked += (_, _) => Touched();
            row.Period.Unchecked += (_, _) => Touched();
            line.Children.Add(row.Period);
            return line;
        }
        row.From = Ui.DateBox(start).Also(d => { d.Width = 150; });
        row.To = Ui.DateBox(end).Also(d => { d.Width = 150; });
        System.Windows.Automation.AutomationProperties.SetName(row.From.TextBox, "Service from");
        System.Windows.Automation.AutomationProperties.SetName(row.To.TextBox, "Service to");
        row.From.Changed += Touched;
        row.To.Changed += Touched;
        var clear = Ui.Link("Clear", () => { row.From.Date = null; row.To.Date = null; }, bold: false).Also(l => { l.FontSize = 13; l.VerticalAlignment = VerticalAlignment.Center; });
        line.Children.Add(Ui.Muted("Service dates", 13).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); }));
        line.Children.Add(row.From);
        line.Children.Add(Ui.Muted("to", 13).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(8, 0, 8, 0); }));
        line.Children.Add(row.To);
        line.Children.Add(clear.Also(l => l.Margin = new Thickness(12, 0, 0, 0)));
        return line;
    }

    private void Touched()
    {
        if (_suppress) return;
        IsDirty = true;
        Refresh();
    }

    private static TextBlock Head(string text, HorizontalAlignment align = HorizontalAlignment.Left) =>
        new() { Text = text.ToUpperInvariant(), Style = Ui.Style("TableHeaderText"), HorizontalAlignment = align };

    private Grid RowGrid(UIElement desc, UIElement qty, UIElement unit, UIElement action, bool totalSpansQty = false)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_dates == LineDates.None ? 56 : 92) });
        var cells = new[] { desc, qty, unit, action };
        for (int i = 0; i < 4; i++)
        {
            var b = new Border { Padding = new Thickness(i == 0 ? 16 : 8, 8, i == 3 ? 12 : 8, 8), Child = cells[i] };
            if (cells[i] is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, i);
            g.Children.Add(b);
        }
        return g;
    }
}
