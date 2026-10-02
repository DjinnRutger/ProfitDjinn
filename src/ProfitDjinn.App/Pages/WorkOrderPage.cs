using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// A customer's rolling work order tab: quick-add, open work grouped by project, billed
/// history, totals and notes (1.x work_orders/detail.html). Opening it creates the tab the
/// first time, as 1.x did.
/// </summary>
public sealed class WorkOrderPage : AppPage
{
    private readonly WorkOrder _wo;
    private readonly string _customer;

    public override string NavKey => "workorders";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Work Orders", () => Shell.Navigate(Routes.WorkOrders(Shell))), new Crumb(_customer) };

    public WorkOrderPage(MainWindow shell, long customerId) : base(shell)
    {
        _wo = Store.WorkOrders.ForCustomer(customerId);
        var wo = _wo;
        _customer = wo.Customer?.Name ?? "";
        double ready = wo.ReadyToBillTotal;
        double defaultRate = Store.WorkOrders.DefaultHourlyRate();

        // ---- header
        var lead = new WrapPanel();
        lead.Children.Add(Ui.Text("Work Order ", "Lead"));
        lead.Children.Add(Ui.Text(wo.Number, "Lead").Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")));
        if (wo.PendingCount > 0) lead.Children.Add(Ui.Text($"  ·  {wo.PendingCount} to-do{(wo.PendingCount != 1 ? "s" : "")}", "Lead"));
        var actions = new List<UIElement> { Ui.Button("Customer", "Btn.OutlineSecondary", "building", () => Shell.Navigate(Routes.Customer(Shell, wo.CustomerId))) };
        if (ready > 0) actions.Add(Ui.Button($"Bill {Ui.Money(ready)}", "Btn.Success", "receipt", () => Shell.Navigate(Routes.Bill(Shell, wo.Id))));
        var header = (DockPanel)Ui.PageHeader(_customer, " ", ready > 0 ? Ui.Badge($"{Ui.Money(ready)} ready to bill", "warningdark", big: true) : null, actions.ToArray());
        var left = (StackPanel)header.Children[1];
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        var page = new StackPanel();
        page.Children.Add(header);

        if (defaultRate <= 0)
        {
            var warn = new WrapPanel();
            warn.Children.Add(new Icon { Glyph = "exclamation-triangle", Size = 14, Margin = new Thickness(0, 2, 8, 0) }.WithResource(Icon.ForegroundProperty, "Alert.Warning.Fg"));
            warn.Children.Add(Ui.Text("Your default hourly rate is not set yet, so labor lines start at $0.00. ", "Body", 14).WithResource(TextBlock.ForegroundProperty, "Alert.Warning.Fg"));
            warn.Children.Add(Ui.Link("Set it in Settings", () => Shell.Navigate(Routes.Settings(Shell, "workorders"))).Also(l => l.FontSize = 14));
            warn.Children.Add(Ui.Text(".", "Body", 14).WithResource(TextBlock.ForegroundProperty, "Alert.Warning.Fg"));
            page.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 20), Child = warn }
                .WithResource(Border.BackgroundProperty, "Alert.Warning.Bg").WithResource(Border.BorderBrushProperty, "Alert.Warning.Border").WithResource(Border.CornerRadiusProperty, "Radius"));
        }

        // ---- main column
        var main = new StackPanel();
        var quickDesc = Ui.TextBox(null, "What needs doing? (e.g. Configure VLANs)").Also(t => t.Style = Ui.Style("Input.Small"));
        var quickLabel = new SuggestBox(wo.OpenLabels, null, "Project (optional)", small: true) { Width = 180 };
        void AddTodo()
        {
            if (quickDesc.Text.Trim().Length == 0) { Shell.ShowError("A description is required."); quickDesc.Focus(); return; }
            Try(() => Shell.Reload(Store.WorkOrders.AddTodo(wo.Id, quickDesc.Text, quickLabel.Text)));
        }
        quickDesc.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTodo(); };
        var quick = new DockPanel();
        var buttons = Ui.Row(8,
            Ui.Button("Add To-Do", "Btn.OutlinePrimary", "plus-lg", AddTodo, small: true),
            Ui.Button("Log Work…", "Btn.Primary", "clock-history", () => WorkLineDialog.Open(Shell, wo, WorkLineDialog.Mode.Log, null, quickDesc.Text, quickLabel.Text), small: true));
        DockPanel.SetDock(buttons, Dock.Right);
        DockPanel.SetDock(quickLabel, Dock.Right);
        quick.Children.Add(buttons.Margin(8, 0, 0, 0));
        quick.Children.Add(quickLabel.Margin(8, 0, 0, 0));
        quick.Children.Add(quickDesc);
        main.Children.Add(Ui.Card(quick, bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        var groups = wo.GroupedOpen();
        foreach (var g in groups) main.Children.Add(GroupCard(g).Margin(0, 0, 0, 24));
        if (groups.Count == 0)
        {
            var empty = (StackPanel)Ui.Empty("clipboard-check", "Nothing on this tab yet.");
            empty.Children.Add(Ui.Muted("Add a to-do above, or click Log Work… to record work you've already done.", 14).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 4, 0, 0); }));
            main.Children.Add(Ui.Card(empty).Margin(0, 0, 0, 24));
        }

        var billed = wo.GroupedBilled(Store.WorkOrders.BilledInvoices(wo));
        if (billed.Count > 0) main.Children.Add(BilledHistory(billed));

        // ---- side column
        var stats = Ui.Stack(16,
            Tile(Ui.Money(ready), "Ready to Bill", "warning", ready <= 0),
            Tile(wo.PendingCount.ToString(), "To-Dos", "primary", wo.PendingCount == 0),
            Tile(Ui.Money(wo.BilledTotal), "Billed to Date", "success", wo.BilledTotal <= 0));
        var notes = Ui.TextArea(wo.Notes, 100, "Gate codes, contacts, anything for next time…").Also(t => t.FontSize = 14);
        var notesCard = Ui.Card(Ui.Stack(8, notes, Ui.Button("Save Notes", "Btn.Primary", "save",
            () => Try(() => Shell.Reload(Store.WorkOrders.UpdateNotes(wo.Id, notes.Text))), small: true).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)),
            "Work Order Notes", "sticky", "Warning");
        var side = Ui.Stack(24, stats, notesCard);

        page.Children.Add(Ui.Columns(24, (Ui.Star(2), main), (Ui.Star(1), side)));
        Content = page;
        Loaded += (_, _) => quickDesc.Focus();
    }

    private FrameworkElement GroupCard(LineGroup g)
    {
        var columns = new List<Column<WorkOrderLine>>
        {
            new("", Ui.Px(42), l => StatusIcon(l), HorizontalAlignment.Center),
            new("Description", Ui.Star(), l => DescriptionCell(l, showBadges: true)),
            new("Date", Ui.Px(110), l => Ui.Muted(l.DatePerformed is { } d ? Ui.Date(d) : "—", 13.6)),
            new("Qty × Rate", Ui.Px(150), l => Ui.Muted(l.EffectiveStatus == LineStatus.Completed ? $"{l.QuantityLabel} × {Ui.Money(l.Rate)}" : "—", 13.6), HorizontalAlignment.Right),
            new("Amount", Ui.Px(100), l => l.EffectiveStatus != LineStatus.Completed ? Ui.Muted("—")
                : l.NoCharge ? Ui.Muted(Ui.Money(l.Amount)).Also(t => t.TextDecorations = TextDecorations.Strikethrough)
                : Ui.Text(Ui.Money(l.Amount), "Money"), HorizontalAlignment.Right),
            new("", Ui.Px(150), l => LineActions(l), HorizontalAlignment.Right),
        };
        var right = Ui.Row(8);
        if (g.PendingCount > 0) right.Children.Add(Ui.Badge($"{g.PendingCount} to-do{(g.PendingCount != 1 ? "s" : "")}", "secondary"));
        if (g.ReadyTotal > 0)
        {
            right.Children.Add(Ui.Badge($"{Ui.Money(g.ReadyTotal)} ready", "warningdark").Margin(8, 0, 0, 0));
            if (g.Label != WorkOrder.GeneralLabel)
                right.Children.Add(Ui.IconButton("receipt", "Btn.OutlineSuccess", "Bill just this project", () => Shell.Navigate(Routes.Bill(Shell, _wo.Id, g.Label))).Margin(8, 0, 0, 0));
        }
        return Ui.Card(Table.Build(columns, g.Lines, rowBrush: l => l.EffectiveStatus == LineStatus.Pending ? "PendingRow" : null),
            g.Label, "folder2-open", headerRight: right, bodyPadding: new Thickness(0));
    }

    private FrameworkElement LineActions(WorkOrderLine l)
    {
        var row = Ui.Row(4);
        if (l.EffectiveStatus == LineStatus.Pending)
            row.Children.Add(Ui.IconButton("check-lg", "Btn.OutlineSuccess", "Mark complete", () => WorkLineDialog.Open(Shell, _wo, WorkLineDialog.Mode.Complete, l)));
        else
        {
            row.Children.Add(Ui.IconButton("arrow-counterclockwise", "Btn.OutlineSecondary", "Back to pending", async () =>
            {
                if (await Shell.Confirm("Move this back to a pending to-do? The logged hours will be cleared.", "Move back"))
                    Try(() => Shell.Reload(Store.WorkOrders.ReopenLine(l.Id)));
            }));
            row.Children.Add(Ui.IconButton(l.NoCharge ? "currency-dollar" : "slash-circle", "Btn.OutlineInfo", l.NoCharge ? "Make billable" : "Mark no charge",
                () => Try(() => Shell.Reload(Store.WorkOrders.ToggleNoCharge(l.Id)))).Margin(4, 0, 0, 0));
        }
        row.Children.Add(Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => WorkLineDialog.Open(Shell, _wo, WorkLineDialog.Mode.Edit, l)).Margin(4, 0, 0, 0));
        row.Children.Add(Ui.IconButton("trash3", "Btn.OutlineDanger", "Delete", async () =>
        {
            if (await Shell.Confirm("Delete this line? This cannot be undone.", "Delete", danger: true))
                Try(() => Shell.Reload(Store.WorkOrders.DeleteLine(l.Id)));
        }).Margin(4, 0, 0, 0));
        return row;
    }

    private FrameworkElement BilledHistory(IReadOnlyList<BilledGroup> groups)
    {
        var all = new List<(object Row, bool Group)>();
        foreach (var g in groups)
        {
            all.Add((g, true));
            foreach (var l in g.Lines) all.Add((l, false));
        }
        var cells = new List<Column<(object Row, bool Group)>>
        {
            new("Invoice", Ui.Px(120), r => r.Row is BilledGroup g
                ? (g.Invoice is { } inv ? Ui.Link(inv.InvoiceNumber, () => Shell.Navigate(Routes.Invoice(Shell, inv.Id)), mono: true) : Ui.Muted("deleted").Also(t => t.ToolTip = "Invoice no longer exists"))
                : new TextBlock()),
            new("Work", Ui.Star(), r =>
            {
                if (r.Row is BilledGroup g) return Ui.Muted($"{g.Lines.Count} line{(g.Lines.Count != 1 ? "s" : "")}", 13.6);
                var l = (WorkOrderLine)r.Row;
                var p = new WrapPanel();
                if (!string.IsNullOrEmpty(l.ProjectLabel)) p.Children.Add(Ui.Badge(l.ProjectLabel, "secondary50").Margin(0, 0, 4, 0));
                p.Children.Add(Ui.Text(l.Description, "Body", 13.6));
                if (l.NoCharge) p.Children.Add(Ui.Badge("No Charge", "info75").Margin(4, 0, 0, 0));
                return p;
            }),
            new("Billed", Ui.Px(110), r => r.Row is BilledGroup g ? Ui.Muted(g.BilledAt is { } b ? Ui.Date(b) : "—", 13.6)
                : Ui.Muted(((WorkOrderLine)r.Row).DatePerformed is { } d ? d.ToString("MMM dd", System.Globalization.CultureInfo.InvariantCulture) : "—", 13.6)),
            new("Amount", Ui.Px(100), r => r.Row is BilledGroup g ? Ui.Text(Ui.Money(g.Total), "Money") : Ui.Muted(Ui.Money(((WorkOrderLine)r.Row).Amount), 13.6), HorizontalAlignment.Right),
        };
        var table = Table.Build(cells, all, rowBrush: r => r.Group ? "TotalsRow" : null);
        table.Visibility = Visibility.Collapsed;

        var chevron = new Icon { Glyph = "chevron-right", Size = 13 }.WithResource(Icon.ForegroundProperty, "TextMuted");
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        toggle.Children.Add(chevron);
        toggle.Children.Add(new Icon { Glyph = "archive", Size = 15, Margin = new Thickness(8, 0, 8, 0) }.WithResource(Icon.ForegroundProperty, "Success"));
        toggle.Children.Add(Ui.Text("Billed History", "Strong", 14.4));
        toggle.MouseLeftButtonUp += (_, _) =>
        {
            bool open = table.Visibility == Visibility.Visible;
            table.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            chevron.Glyph = open ? "chevron-right" : "chevron-down";
        };
        int count = _wo.BilledLines.Count();
        var card = Ui.Card(table, "Billed History", headerRight: Ui.Badge($"{count} line{(count != 1 ? "s" : "")} · {Ui.Money(_wo.BilledTotal)}", "secondary"), bodyPadding: new Thickness(0));
        card.HeaderLeft = toggle;
        return card;
    }

    private static StatTile Tile(string value, string label, string tone, bool muted) => new()
    {
        Value = value, Label = label, Tone = tone, Glyph = "", ValueSize = 22.4, Muted = muted, Focusable = false, Cursor = Cursors.Arrow,
    };

    private static FrameworkElement StatusIcon(WorkOrderLine l)
    {
        var (glyph, brush, tip) = l.EffectiveStatus == LineStatus.Pending ? ("square", "TextMuted", "Pending")
            : l.NoCharge ? ("check-square", "Info", "No charge") : ("check-square", "Success", "Ready to bill");
        return new Icon { Glyph = glyph, Size = 15, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Center }.WithResource(Icon.ForegroundProperty, brush);
    }

    /// <summary>Description with its No Charge / type badges and the internal-note marker.</summary>
    public static FrameworkElement DescriptionCell(WorkOrderLine l, bool showBadges)
    {
        var p = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var text = Ui.Text(l.Description, "Body");
        if (showBadges && l.EffectiveStatus == LineStatus.Pending) text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        p.Children.Add(text);
        if (showBadges && l.NoCharge) p.Children.Add(Ui.Badge("No Charge", "info75").Margin(6, 0, 0, 0));
        if (showBadges && l.EffectiveStatus == LineStatus.Completed && l.LineType != LineTypes.Labor) p.Children.Add(Ui.Badge(l.TypeLabel, "secondary50").Margin(6, 0, 0, 0));
        if (!string.IsNullOrEmpty(l.InternalNote))
            p.Children.Add(new Icon { Glyph = "sticky", Size = 13, Margin = new Thickness(6, 0, 0, 0), ToolTip = l.InternalNote }.WithResource(Icon.ForegroundProperty, "Warning"));
        return p;
    }

    public static string StatusKind(WorkOrderLine l) => l.EffectiveStatus switch
    {
        LineStatus.Billed => "success",
        LineStatus.Completed => l.NoCharge ? "info" : "warning",
        _ => "secondary",
    };
}
