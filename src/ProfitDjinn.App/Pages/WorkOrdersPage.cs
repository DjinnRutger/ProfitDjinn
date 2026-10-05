using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Pages;

/// <summary>Open work across every customer, one collapsible card per tab (1.x work_orders/list.html).</summary>
public sealed class WorkOrdersPage : AppPage
{
    private readonly bool _all;
    private readonly string _search;

    public override string NavKey => "workorders";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Work Orders") };

    public WorkOrdersPage(MainWindow shell, bool all, string search) : base(shell)
    {
        _all = all;
        _search = search;
        var orders = Store.WorkOrders.List(search, all);
        double grandReady = PyMath.Sum(orders, w => w.ReadyToBillTotal);
        int grandPending = orders.Sum(w => w.PendingCount);

        var lead = new WrapPanel();
        if (orders.Count > 0)
        {
            lead.Children.Add(Ui.Text($"{orders.Count} customer{(orders.Count != 1 ? "s" : "")} with activity", "Lead"));
            if (grandReady > 0)
            {
                lead.Children.Add(Ui.Text("  ·  ", "Lead"));
                lead.Children.Add(Ui.Text($"{Ui.Money(grandReady)} ready to bill", "Strong", 14).WithResource(TextBlock.ForegroundProperty, "SuccessText"));
            }
            if (grandPending > 0) lead.Children.Add(Ui.Text($"  ·  {grandPending} to-do{(grandPending != 1 ? "s" : "")}", "Lead"));
        }
        else lead.Children.Add(Ui.Text("Nothing waiting to be billed.", "Lead"));

        var header = (DockPanel)Ui.PageHeader("Open Work", " ", null,
            Ui.Button("Customers", "Btn.OutlineSecondary", "building", () => Shell.Navigate(Routes.Customers(Shell))));
        var left = (StackPanel)header.Children[1];
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        // filter + search
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(FilterButton("Open Work", "clipboard-check", "Btn.OutlineWarning", "Warning", !all, () => Run(false, _search)));
        tabs.Children.Add(FilterButton("All Tabs", null, "Btn.OutlineSecondary", "Secondary", all, () => Run(true, _search)));
        var box = Ui.TextBox(search, "Search customer, project or work…", 280).Also(t => { t.Style = Ui.Style("Input.Small"); Input.SetPrefixGlyph(t, "search"); });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(_all, box.Text); };
        var searchRow = Ui.Row(8, box);
        if (search.Length > 0) searchRow.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run(_all, ""), small: true).Margin(-8, 0, 0, 0));
        searchRow.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(_all, box.Text), small: true));

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Card(Ui.Row(16, tabs, searchRow), bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        foreach (var wo in orders) page.Children.Add(WorkOrderCard(wo).Margin(0, 0, 0, 16));
        if (orders.Count == 0)
        {
            var emptyBox = (Border)Ui.Empty("clipboard-check", search.Length > 0
                ? $"No work orders match “{search}”."
                : "No open work. Open a customer and start logging work so it doesn't get forgotten.");
            ((StackPanel)emptyBox.Child).Children.Add(Ui.Link("Browse customers", () => Shell.Navigate(Routes.Customers(Shell)), bold: false)
                .Also(l => { l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 8, 0, 0); }));
            page.Children.Add(Ui.Card(emptyBox));
        }
        Content = page;
    }

    private FrameworkElement WorkOrderCard(WorkOrder wo)
    {
        double ready = wo.ReadyToBillTotal;
        var lines = wo.CompletedLines.Concat(wo.PendingLines).ToList();
        var columns = new List<Column<WorkOrderLine>>
        {
            new("Project", Ui.Px(130), l => string.IsNullOrEmpty(l.ProjectLabel) ? Ui.Muted("—") : Ui.Badge(l.ProjectLabel, "secondary50")),
            new("Description", Ui.Star(), l => WorkOrderPage.DescriptionCell(l, showBadges: false)),
            new("Date", Ui.Px(110), l => Ui.Muted(l.DatePerformed is { } d ? Ui.Date(d) : "—", 13.6)),
            new("Qty", Ui.Px(110), l => Ui.Muted(l.EffectiveStatus == LineStatus.Completed ? l.QuantityLabel : "—", 13.6), HorizontalAlignment.Right),
            new("Amount", Ui.Px(110), l => l.EffectiveStatus != LineStatus.Completed ? Ui.Muted("—")
                : l.NoCharge ? Ui.Muted("$0.00") : Ui.Text(Ui.Money(l.Amount), "Money"), HorizontalAlignment.Right),
            new("Status", Ui.Px(120), l => Ui.Badge(l.StatusLabel, WorkOrderPage.StatusKind(l)), HorizontalAlignment.Center),
        };
        FrameworkElement body = lines.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<WorkOrderLine>()), Ui.Empty("check2-circle", "Nothing open on this tab."))
            : Table.Build(columns, lines);

        var chevron = new Icon { Glyph = "chevron-down", Size = 13 }.WithResource(Icon.ForegroundProperty, "TextMuted");
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        toggle.Children.Add(chevron);
        toggle.Children.Add(Ui.Text(wo.Customer?.Name ?? "", "Strong", 14.4).Margin(8, 0, 0, 0));
        toggle.Children.Add(Ui.Muted(wo.Number, 12.5).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")).Margin(8, 0, 0, 0));

        var right = Ui.Row(8);
        if (ready > 0) right.Children.Add(Ui.Badge($"{Ui.Money(ready)} ready", "warningdark"));
        if (wo.PendingCount > 0) right.Children.Add(Ui.Badge($"{wo.PendingCount} to-do{(wo.PendingCount != 1 ? "s" : "")}", "secondary").Margin(8, 0, 0, 0));
        right.Children.Add(Ui.IconButton("box-arrow-up-right", "Btn.OutlineSecondary", "Open tab", () => Shell.Navigate(Routes.WorkOrder(Shell, wo.CustomerId))).Margin(8, 0, 0, 0));
        if (ready > 0) right.Children.Add(Ui.Button($"Bill {Ui.Money(ready)}", "Btn.Success", "receipt", () => Shell.Navigate(Routes.Bill(Shell, wo.Id)), small: true).Margin(8, 0, 0, 0));

        var card = Ui.Card(body, title: wo.Customer?.Name, headerRight: right, bodyPadding: new Thickness(0));
        card.HeaderLeft = toggle;
        toggle.MouseLeftButtonUp += (_, _) =>
        {
            bool open = body.Visibility == Visibility.Visible;
            body.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            chevron.Glyph = open ? "chevron-right" : "chevron-down";
        };
        return card;
    }

    private static Button FilterButton(string text, string? glyph, string style, string activeBrush, bool active, Action onClick)
    {
        var b = Ui.Button(text, style, glyph, onClick, small: true);
        if (active)
        {
            b.SetResourceReference(Button.BackgroundProperty, activeBrush);
            b.Foreground = activeBrush == "Warning" ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        }
        return b;
    }

    private void Run(bool all, string search) => Shell.Navigate(Routes.WorkOrders(Shell, all, search.Trim()));
}
