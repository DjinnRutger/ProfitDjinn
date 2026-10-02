using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// Turn completed work into an invoice (1.x work_orders/bill.html): pick the work, pick how
/// it rolls up, adjust the invoice lines, create. Unchecked work stays on the tab.
/// </summary>
public sealed class BillPage : AppPage
{
    private readonly BillSetup _setup;
    private readonly long _workOrderId;
    private readonly string? _label;
    private readonly Dictionary<long, CheckBox> _picks = new();
    private readonly Dictionary<string, CheckBox> _labelToggles = new();
    private readonly LineBuilder _lines = new("Select some work above to build the invoice.");
    private readonly TextBlock _selectedTotal = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _summaryTotal = new() { FontWeight = FontWeights.Bold, FontSize = 24 };
    private readonly TextBlock _summaryCount = new() { FontWeight = FontWeights.SemiBold, FontSize = 14.4, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _hint;
    private readonly Border _mismatch;
    private readonly TextBlock _mismatchText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.8 };
    private readonly List<Button> _modeButtons = new();
    private readonly TextBox _number, _notes, _term1, _term2;
    private readonly DateBox _date;
    private readonly Field _numberField, _dateField;
    private RollupMode _mode = RollupMode.One;
    private bool _syncing;

    public override string NavKey => "workorders";

    public override IReadOnlyList<Crumb> Crumbs => new[]
    {
        new Crumb("Work Orders", () => Shell.Navigate(Routes.WorkOrders(Shell))),
        new Crumb(_setup.WorkOrder.Customer?.Name ?? "", () => Shell.Navigate(Routes.WorkOrder(Shell, _setup.WorkOrder.CustomerId))),
        new Crumb("Bill"),
    };

    public BillPage(MainWindow shell, long workOrderId, string? label) : base(shell)
    {
        _workOrderId = workOrderId;
        _label = label;
        _setup = Store.Billing.Prepare(workOrderId, label);
        var wo = _setup.WorkOrder;
        string customer = wo.Customer?.Name ?? "";

        // ---- header
        var lead = new WrapPanel();
        lead.Children.Add(Ui.Text(customer, "Lead"));
        if (_setup.LabelFilter is { } lf)
        {
            lead.Children.Add(Ui.Text("  ·  ", "Lead"));
            lead.Children.Add(Ui.Badge(lf, "secondary50"));
        }
        var header = (DockPanel)Ui.PageHeader("Bill Work Order", " ", null,
            Ui.Button("Back to Tab", "Btn.OutlineSecondary", "arrow-left", () => Shell.Navigate(Routes.WorkOrder(Shell, wo.CustomerId))));
        var hl = (StackPanel)header.Children[1];
        hl.Children.RemoveAt(1);
        hl.Children.Add(lead.Margin(0, 4, 0, 0));

        // ---- work to bill
        var workRows = new List<object>();
        string current = "\0";
        foreach (var line in _setup.Lines)
        {
            if (line.LabelOrGeneral != current) { current = line.LabelOrGeneral; workRows.Add(current); }
            workRows.Add(line);
        }
        foreach (var line in _setup.Lines)
        {
            var cb = new CheckBox { IsChecked = _setup.PreselectedIds.Contains(line.Id), HorizontalAlignment = HorizontalAlignment.Center };
            cb.Click += (_, _) => SelectionChanged();
            _picks[line.Id] = cb;
        }
        foreach (string lbl in _setup.Lines.Select(l => l.LabelOrGeneral).Distinct())
        {
            var t = new CheckBox { IsThreeState = false, ToolTip = "Toggle this project", HorizontalAlignment = HorizontalAlignment.Center };
            string captured = lbl;
            t.Click += (_, _) =>
            {
                bool on = t.IsChecked == true;
                foreach (var l in _setup.Lines.Where(x => x.LabelOrGeneral == captured)) _picks[l.Id].IsChecked = on;
                SelectionChanged();
            };
            _labelToggles[lbl] = t;
        }
        var workCols = new List<Column<object>>
        {
            new("", Ui.Px(42), r => r is string lbl ? _labelToggles[lbl] : _picks[((WorkOrderLine)r).Id], HorizontalAlignment.Center),
            new("Description", Ui.Star(), r => r is string lbl ? Ui.Text(lbl, "Strong", 12.8) : WorkDescription((WorkOrderLine)r)),
            new("Date", Ui.Px(105), r => r is string ? new TextBlock() : Ui.Muted(((WorkOrderLine)r).DatePerformed is { } d ? Ui.Date(d) : "—", 13.6)),
            new("Qty × Rate", Ui.Px(140), r => r is WorkOrderLine l ? Ui.Muted($"{l.QuantityLabel} × {Ui.Money(l.Rate)}", 13.6) : new TextBlock(), HorizontalAlignment.Right),
            new("Amount", Ui.Px(100), r => r is WorkOrderLine l
                ? (l.NoCharge ? Ui.Muted(Ui.Money(l.Amount)).Also(t => t.TextDecorations = TextDecorations.Strikethrough) : Ui.Text(Ui.Money(l.Amount), "Money"))
                : new TextBlock(), HorizontalAlignment.Right),
        };
        var workFooter = new List<UIElement?[]> { new UIElement?[] { null, null, null, Ui.Bold("Selected").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right), _selectedTotal } };
        var workTable = Table.Build(workCols, workRows, rowBrush: r => r is string ? "TotalsRow" : null, footer: workFooter);
        var workNote = new Border { Padding = new Thickness(16, 8, 16, 8), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = Ui.Row(6, new Icon { Glyph = "info-circle", Size = 12 }.WithResource(Icon.ForegroundProperty, "TextMuted"), Ui.Muted("Anything you leave unchecked stays on the tab for next time.", 12.8)) }
            .WithResource(Border.BorderBrushProperty, "Border");
        var workCard = Ui.Card(Ui.Stack(0, workTable, workNote), "Work to Bill", "check2-square",
            headerRight: Ui.Row(8,
                Ui.Button("All", "Btn.OutlineSecondary", null, () => SetAll(true), small: true),
                Ui.Button("None", "Btn.OutlineSecondary", null, () => SetAll(false), small: true)),
            bodyPadding: new Thickness(0));

        // ---- roll-up style
        var modes = new UniformGridRow();
        foreach (var (m, text) in new[] { (RollupMode.One, "One summary line"), (RollupMode.Type, "Grouped by type"), (RollupMode.Detailed, "Every line") })
        {
            var b = new Button { Content = text, Style = Ui.Style("Btn.OutlinePrimary"), Tag = m, HorizontalAlignment = HorizontalAlignment.Stretch };
            b.Click += async (_, _) => { _mode = m; ShowMode(); await Regenerate(false); };
            _modeButtons.Add(b);
            modes.Add(b);
        }
        _hint = Ui.Muted("", 12.8).Margin(0, 8, 0, 0);
        var rollupCard = Ui.Card(Ui.Stack(0, modes, _hint), "How should this appear on the invoice?", "arrows-collapse");

        // ---- invoice lines
        _lines.Changed += UpdateTotals;
        var linesCard = Ui.Card(_lines, "Invoice Lines", "list-ul",
            headerRight: Ui.Row(8,
                Ui.Button("Rebuild", "Btn.OutlineSecondary", "arrow-clockwise", async () => await Regenerate(false), small: true),
                Ui.Button("Add Line", "Btn.OutlinePrimary", "plus-lg", () => _lines.AddRow("", "1", "0.00", focus: true), small: true)),
            bodyPadding: new Thickness(0));

        _notes = Ui.TextArea("", 56);
        _term1 = Ui.TextBox(_setup.Term1).Also(t => t.Style = Ui.Style("Input.Small"));
        _term2 = Ui.TextBox(_setup.Term2).Also(t => t.Style = Ui.Style("Input.Small"));
        var termsCard = Ui.Card(Ui.Stack(0, Ui.Field("Notes", _notes),
            Ui.Columns(16, (Ui.Star(), Ui.Field("Payment Terms", _term1).Margin(0, 0, 0, 0)), (Ui.Star(), Ui.Field("Additional Terms", _term2).Margin(0, 0, 0, 0)))));

        var main = Ui.Stack(24, workCard, rollupCard, linesCard, termsCard);

        // ---- summary
        _number = Ui.TextBox(_setup.InvoiceNumber).Also(t => t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"));
        _date = Ui.DateBox(_setup.Date);
        _numberField = Ui.Field("Invoice #", _number);
        _dateField = Ui.Field("Date", _date);
        var facts = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        facts.RowDefinitions.Add(new RowDefinition());
        facts.RowDefinitions.Add(new RowDefinition());
        var c1 = Ui.Muted("Customer", 14.4);
        var c2 = Ui.Text(customer, "Strong", 14.4).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        var c3 = Ui.Muted("Work lines", 14.4).Margin(0, 8, 0, 0);
        _summaryCount.Margin = new Thickness(0, 8, 0, 0);
        _summaryCount.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Grid.SetColumn(c2, 1); Grid.SetRow(c3, 1); Grid.SetRow(_summaryCount, 1); Grid.SetColumn(_summaryCount, 1);
        facts.Children.Add(c1); facts.Children.Add(c2); facts.Children.Add(c3); facts.Children.Add(_summaryCount);

        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_summaryTotal, Dock.Right);
        _summaryTotal.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        totalRow.Children.Add(_summaryTotal);
        totalRow.Children.Add(Ui.Muted("Invoice Total", 15).Also(t => t.VerticalAlignment = VerticalAlignment.Center));

        _mismatchText.SetResourceReference(TextBlock.ForegroundProperty, "Alert.Info.Fg");
        _mismatch = new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16), Child = _mismatchText, Visibility = Visibility.Collapsed }
            .WithResource(Border.BackgroundProperty, "Alert.Info.Bg").WithResource(Border.BorderBrushProperty, "Alert.Info.Border").WithResource(Border.CornerRadiusProperty, "Radius");

        var create = Ui.Button("Create Invoice", "Btn.Primary", "receipt", Create).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Padding = new Thickness(16, 9, 16, 9); b.FontSize = 17.6; });
        var cancel = Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Routes.WorkOrder(Shell, wo.CustomerId))).Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(0, 8, 0, 0);
        var summary = Ui.Stack(0, _numberField, _dateField, facts, totalRow, _mismatch, new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 0, 0, 16) }, create, cancel);
        var side = Ui.Card(summary, "New Invoice");

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), main), (Ui.Star(1), side)));
        Content = page;

        ShowMode();
        SyncSelection();
        _ = Regenerate(true);
    }

    private static FrameworkElement WorkDescription(WorkOrderLine l)
    {
        var p = new WrapPanel();
        p.Children.Add(Ui.Text(l.Description, "Body"));
        if (l.NoCharge) p.Children.Add(Ui.Badge("No Charge", "info75").Margin(6, 0, 0, 0));
        if (l.LineType != LineTypes.Labor) p.Children.Add(Ui.Badge(l.TypeLabel, "secondary50").Margin(6, 0, 0, 0));
        return p;
    }

    private List<WorkOrderLine> Selected() => _setup.Lines.Where(l => _picks[l.Id].IsChecked == true).ToList();

    private void SetAll(bool on)
    {
        foreach (var cb in _picks.Values) cb.IsChecked = on;
        SelectionChanged();
    }

    private async void SelectionChanged()
    {
        SyncSelection();
        await Regenerate(false);
    }

    private void SyncSelection()
    {
        var sel = Selected();
        _selectedTotal.Text = Ui.MoneyGrouped(Rollup.SelectedTotal(sel));
        _summaryCount.Text = sel.Count.ToString();
        foreach (var (lbl, toggle) in _labelToggles)
        {
            var boxes = _setup.Lines.Where(l => l.LabelOrGeneral == lbl).Select(l => _picks[l.Id]).ToList();
            int on = boxes.Count(b => b.IsChecked == true);
            toggle.IsChecked = on == 0 ? false : on == boxes.Count ? true : null;   // null = indeterminate
        }
        UpdateTotals();
    }

    /// <summary>Rebuilds the invoice lines from the selection, asking first if they were edited by hand.</summary>
    private async Task Regenerate(bool force)
    {
        if (_lines.IsDirty && !force &&
            !await Shell.Confirm("Rebuild the invoice lines from your selection? Your edits will be lost.", "Rebuild"))
            return;
        _lines.SetRows(Rollup.Build(_mode, Selected()));
        UpdateTotals();
    }

    private void ShowMode()
    {
        foreach (var b in _modeButtons)
        {
            bool on = (RollupMode)b.Tag == _mode;
            if (on) { b.SetResourceReference(Button.BackgroundProperty, "BsPrimary"); b.Foreground = System.Windows.Media.Brushes.White; }
            else { b.Background = System.Windows.Media.Brushes.Transparent; b.SetResourceReference(Button.ForegroundProperty, "BsPrimary"); }
        }
        _hint.Text = Rollup.Hints[_mode];
    }

    private void UpdateTotals()
    {
        if (_syncing) return;
        _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);
        double? diff = Rollup.Mismatch(_lines.Rows, Selected());
        if (diff is { } d)
        {
            _mismatchText.Text = $"The invoice total differs from the selected work ({(d > 0 ? "+" : "−")}{Ui.MoneyGrouped(Math.Abs(d))}). That's fine if you meant to adjust it.";
            _mismatch.Visibility = Visibility.Visible;
        }
        else _mismatch.Visibility = Visibility.Collapsed;
    }

    private void Create()
    {
        _numberField.Error = _dateField.Error = null;
        var sel = Selected();
        if (sel.Count == 0) { Shell.ShowError("Select at least one piece of work to bill."); return; }
        if (_lines.Count == 0) { Shell.ShowError("The invoice needs at least one line item."); return; }
        if (!_date.IsBlank && _date.Date is null) { _dateField.Error = "Not a valid date value."; return; }
        try
        {
            var created = Store.Billing.Submit(_workOrderId, new BillHeader(_number.Text, _date.Date, _notes.Text, _term1.Text, _term2.Text),
                sel.Select(l => l.Id).ToList(), _lines.Rows);
            Shell.Navigate(Routes.Invoice(Shell, created.Id), created.Notice);
        }
        catch (SelectionChangedException ex)
        {
            Shell.Navigate(Routes.Bill(Shell, _workOrderId), Notice.Warning(ex.Message));
        }
        catch (ValidationException v)
        {
            if (v.Fields.TryGetValue("invoice_number", out var n)) _numberField.Error = n;
            if (v.Fields.TryGetValue("date", out var d)) _dateField.Error = d;
            Shell.ShowError(v.Message);
        }
        catch (UserFacingException ex)
        {
            Shell.ShowError(ex.Message);
        }
    }

    /// <summary>Bootstrap btn-group w-100: equal-width buttons joined in a row.</summary>
    private sealed class UniformGridRow : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGridRow() => Rows = 1;
        public void Add(Button b) { b.Margin = new Thickness(Children.Count == 0 ? 0 : -1, 0, 0, 0); Children.Add(b); }
    }
}
