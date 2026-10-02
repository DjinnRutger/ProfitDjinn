using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// Every control style on one page, for checking the look against 1.x in each theme.
/// Hidden: open it with Ctrl+Shift+G.
/// </summary>
public sealed class GalleryPage : AppPage
{
    public override string NavKey => "";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Style gallery") };

    private sealed record Sample(string Number, string Customer, string Date, double Amount, string Status, string Kind);

    public GalleryPage(MainWindow shell) : base(shell)
    {
        var p = new StackPanel();
        p.Children.Add(Ui.PageHeader("Style Gallery", "Every control, in the current theme.", Ui.Badge("Partial", "info", big: true),
            Ui.Button("Primary", "Btn.Primary", "plus-lg", () => { }),
            Ui.Button("Ghost", "Btn.Ghost", "pencil", () => { })));

        var buttons = new WrapPanel();
        foreach (var (style, text, glyph) in new[]
        {
            ("Btn.Primary", "Primary", "plus-lg"), ("Btn.Success", "Success", "cash-coin"), ("Btn.Secondary", "Secondary", null),
            ("Btn.Danger", "Danger", "trash3"), ("Btn.Ghost", "Ghost", "pencil"), ("Btn.OutlineSecondary", "Outline secondary", null),
            ("Btn.OutlinePrimary", "Outline primary", "eye"), ("Btn.OutlineSuccess", "Outline success", "check-lg"),
            ("Btn.OutlineDanger", "Outline danger", "trash3"), ("Btn.OutlineWarning", "Outline warning", "arrow-counterclockwise"),
            ("Btn.OutlineInfo", "Outline info", "slash-circle"),
        })
        {
            buttons.Children.Add(Ui.Button(text, style, glyph, () => { }).Margin(0, 0, 8, 8));
            buttons.Children.Add(Ui.Button(text, style, glyph, () => { }, small: true).Margin(0, 0, 16, 8));
        }
        buttons.Children.Add(Ui.IconButton("eye", "Btn.OutlinePrimary", "View", () => { }).Margin(0, 0, 4, 8));
        buttons.Children.Add(Ui.IconButton("pencil", "Btn.OutlineSecondary", "Edit", () => { }).Margin(0, 0, 4, 8));
        buttons.Children.Add(Ui.IconButton("trash3", "Btn.OutlineDanger", "Delete", () => { }).Margin(0, 0, 4, 8));
        p.Children.Add(Ui.Card(buttons, "Buttons", "grid-3x3").Margin(0, 0, 0, 24));

        var badges = new WrapPanel();
        foreach (var (text, kind) in new[] { ("Paid", "success"), ("Partial", "info"), ("Unpaid", "warning"), ("Ready to Bill", "warning"),
                     ("No Charge", "info75"), ("Pending", "secondary"), ("Active", "success75"), ("$120.00 ready", "warningdark"),
                     ("Network", "secondary50"), ("Admin", "primary"), ("Overdue", "danger") })
            badges.Children.Add(Ui.Badge(text, kind).Margin(0, 0, 8, 8));
        p.Children.Add(Ui.Card(badges, "Badges", "tag", "Success").Margin(0, 0, 0, 24));

        var form = Ui.Columns(24,
            (Ui.Star(), Ui.Stack(0,
                Ui.Field("Company / Name", Ui.TextBox("Acme Corp"), required: true),
                Ui.Field("Email", Ui.TextBox(null, "you@example.com")).Also(f => f.Error = "Enter a valid email address."),
                Ui.Field("Amount", Ui.TextBox("120.00").Also(t => Input.SetPrefix(t, "$"))))),
            (Ui.Star(), Ui.Stack(0,
                Ui.Field("Method", new ComboBox { ItemsSource = new[] { "Cash", "Check", "Credit Card" }, SelectedIndex = 1 }),
                Ui.Field("Date", Ui.DateBox(DateOnly.FromDateTime(DateTime.Today))),
                Ui.Field("Notes", Ui.TextArea("Some notes"), hint: "Private. Never printed."))),
            (Ui.Star(), Ui.Stack(12,
                new CheckBox { Content = "Mark as Paid", IsChecked = true },
                new CheckBox { Content = "Indeterminate", IsChecked = null, IsThreeState = true },
                new CheckBox { Content = "Enabled", Style = Ui.Style("Switch"), IsChecked = true },
                new RadioButton { Content = "One summary line", IsChecked = true, GroupName = "g" },
                new RadioButton { Content = "Every line", GroupName = "g" })));
        p.Children.Add(Ui.Card(form, "Forms", "sliders").Margin(0, 0, 0, 24));

        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(-8, 0, -8, 24) };
        tiles.Children.Add(new StatTile { Value = "12", Label = "Customers", Glyph = "building", Margin = new Thickness(8, 0, 8, 0) });
        tiles.Children.Add(new StatTile { Value = "40", Label = "Total Invoices", Glyph = "receipt", Tone = "info", Margin = new Thickness(8, 0, 8, 0) });
        tiles.Children.Add(new StatTile { Value = "$658.29", Label = "Outstanding", Glyph = "exclamation-circle-fill", Tone = "warning", Accent = "warning", Badge = Ui.Badge("4", "warningdark"), Margin = new Thickness(8, 0, 8, 0) });
        tiles.Children.Add(new StatTile { Value = "$0.00", Label = "Unbilled Work", Glyph = "clipboard-check", Tone = "danger", Muted = true, Margin = new Thickness(8, 0, 8, 0) });
        tiles.Children.Add(new StatTile { Value = "$595.10", Label = "2026 Revenue", Glyph = "graph-up-arrow", Tone = "success", Accent = "bottom", Margin = new Thickness(8, 0, 8, 0) });
        p.Children.Add(tiles);

        var rows = new[]
        {
            new Sample("INV0006", "Acme Corp", "May 15, 2026", 477.5, "Unpaid", "warning"),
            new Sample("INV0005", "Acme Corp", "Apr 01, 2026", 40, "Paid", "success"),
            new Sample("INV0003", "Beta LLC", "Mar 05, 2026", 200, "Partial", "info"),
        };
        var columns = new List<Column<Sample>>
        {
            new("Invoice #", Ui.Auto, r => Ui.Link(r.Number, () => { }, mono: true)),
            new("Customer", Ui.Star(), r => Ui.Link(r.Customer, () => { }, bold: false)),
            new("Date", Ui.Auto, r => Ui.Muted(r.Date, 13.6)),
            new("Amount", Ui.Auto, r => Ui.Text(Ui.Money(r.Amount), "Money"), HorizontalAlignment.Right),
            new("Status", Ui.Auto, r => Ui.Badge(r.Status, r.Kind), HorizontalAlignment.Center),
            new("", Ui.Auto, r => Ui.Row(4, Ui.IconButton("eye", "Btn.OutlinePrimary", "View", () => { }), Ui.IconButton("pencil", "Btn.OutlineSecondary", "Edit", () => { })), HorizontalAlignment.Right),
        };
        var footer = new List<UIElement?[]> { new UIElement?[] { null, null, Ui.Bold("Total"), Ui.Text(Ui.Money(717.5), "Money"), null, null } };
        p.Children.Add(Ui.Card(Table.Build(columns, rows, footer: footer), "Recent Invoices", "receipt",
            headerRight: Ui.Button("View All", "Btn.OutlineSecondary", null, () => { }, small: true), bodyPadding: new Thickness(0)).Margin(0, 0, 0, 24));

        p.Children.Add(Ui.Card(Ui.Empty("people", "No customers found.", "Add one now.", () => { })).Margin(0, 0, 0, 24));
        p.Children.Add(Ui.Row(8,
            Ui.Button("Show notices", "Btn.Ghost", "info-circle", () =>
            {
                Shell.ShowNotice(Notice.Success("Invoice INV0007 created."));
                Shell.ShowNotice(Notice.Info("Partial payment of $60.00 recorded. Balance remaining: $140.00."));
                Shell.ShowNotice(Notice.Warning("Invoice INV0007 deleted."));
                Shell.ShowError("Invoice number INV0001 already exists.");
            }),
            Ui.Button("Confirm dialog", "Btn.OutlineDanger", "trash3", async () =>
            {
                if (await Shell.Confirm("Delete invoice INV0007?", "Delete", danger: true)) Shell.ShowNotice(Notice.Warning("Deleted (not really)."));
            })));
        Content = p;
    }
}
