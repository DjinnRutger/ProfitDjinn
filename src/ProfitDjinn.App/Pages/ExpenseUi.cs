using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>A pick-list entry with an id (null = none).</summary>
public sealed record Choice(long? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>2.2. Pieces shared by the expense, vendor and recurring screens.</summary>
public static class ExpenseUi
{
    /// <summary>Paid / Partial / Unpaid as on invoices, plus Overdue when the due date has passed.</summary>
    public static UIElement StatusBadge(Expense e, DateOnly today, bool big = false)
    {
        var row = Ui.Row(4, Ui.Badge(e.StatusLabel, e.Status switch
        {
            InvoiceStatus.Paid => "success",
            InvoiceStatus.Partial => "info",
            _ => "warning",
        }, big));
        if (e.IsOverdue(today)) row.Children.Add(Ui.Badge("Overdue", "danger", big));
        return row;
    }

    /// <summary>
    /// 2.6. The vendor picker: type to pick, empty for no vendor, Add Vendor for a new name. A
    /// current vendor that has been made inactive stays listed (marked) so editing an old
    /// expense does not drop it.
    /// </summary>
    public static RecordPicker VendorPicker(Store store, long? current)
    {
        var items = store.Vendors.ActiveForPicker().Select(v => new PickItem(v.Id, v.Name, v.Contact)).ToList();
        if (current is { } id && items.All(i => i.Id != id))
        {
            try { items.Add(new PickItem(id, store.Vendors.Get(id).Name + " (inactive)")); }
            catch (UserFacingException) { }
        }
        return new RecordPicker(items, current, "Type a vendor name, or leave empty", "Add Vendor");
    }

    /// <summary>"Choose a vendor..." when the box holds a name that is not a vendor; null when it is fine.</summary>
    public static string? VendorProblem(RecordPicker picker) => picker.HasUnmatchedText
        ? "No vendor has that name. Pick one from the list, click Add Vendor, or clear the box for no vendor."
        : null;

    /// <summary>The category picker: shown categories, plus a hidden current one (marked).</summary>
    public static ComboBox CategoryPicker(Store store, long? current, bool allowNone = false)
    {
        var choices = new List<Choice>();
        if (allowNone) choices.Add(new Choice(null, "— None —"));
        choices.AddRange(store.Categories.Active().Select(c => new Choice(c.Id, c.Name)));
        if (current is { } id && choices.All(c => c.Id != id))
        {
            try { choices.Add(new Choice(id, store.Categories.Get(id).Name + " (hidden)")); }
            catch (UserFacingException) { }
        }
        int at = choices.FindIndex(c => c.Id == current);
        return new ComboBox { ItemsSource = choices, SelectedIndex = at >= 0 ? at : allowNone ? 0 : -1 };
    }

    public static long? SelectedId(ComboBox box) => (box.SelectedItem as Choice)?.Id;

    /// <summary>Payment method choices, as the payment dialog lists them.</summary>
    public static ComboBox MethodPicker(string? current)
    {
        var choices = PaymentMethods.All.Select(m => new MethodItem(m.Value, m.Label)).ToList();
        int at = choices.FindIndex(m => m.Value == current);
        return new ComboBox { ItemsSource = choices, SelectedIndex = at >= 0 ? at : 0 };
    }

    public sealed record MethodItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public static string SelectedMethod(ComboBox box) => (box.SelectedItem as MethodItem)?.Value ?? "cash";

    /// <summary>A switch styled like the Settings switches, with its own label.</summary>
    public static CheckBox Switch(string text, bool on) => new() { Style = Ui.Style("Switch"), IsChecked = on, Content = text };

    /// <summary>The columns of an expense table (the list and a vendor's history).</summary>
    /// <param name="showVendor">The expense list shows vendor and category; a vendor's own page shows neither.</param>
    public static List<Column<Expense>> Columns(MainWindow shell, DateOnly today, bool showVendor)
    {
        var cols = new List<Column<Expense>>
        {
            new("Date", Ui.Auto, e => Ui.Muted(Ui.Date(e.Date), 13.6)),
        };
        if (showVendor)
            cols.Add(new("Vendor", Ui.Star(1.2), e => e.VendorId is { } v
                ? Ui.Link(e.VendorName, () => shell.Navigate(Routes.Vendor(shell, v)), bold: false).Also(l => l.TextWrapping = TextWrapping.Wrap)
                : Ui.Muted("—", 13.6)));
        cols.Add(new("Description", Ui.Star(1.6), e =>
        {
            // Icons on the right, the description filling and wrapping to their left.
            var cell = new DockPanel();
            var icons = Ui.Row(4);
            if (e.Receipts.Count > 0) icons.Children.Add(new Icon { Glyph = "paperclip", Size = 13, ToolTip = $"{e.Receipts.Count} receipt(s)" }.WithResource(Icon.ForegroundProperty, "TextMuted"));
            if (e.RecurringId is not null) icons.Children.Add(new Icon { Glyph = "arrow-repeat", Size = 13, ToolTip = "Created by a recurring expense" }.WithResource(Icon.ForegroundProperty, "TextMuted"));
            icons.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(icons, Dock.Right);
            cell.Children.Add(icons);
            cell.Children.Add(Ui.Link(e.Description, () => shell.Navigate(Routes.Expense(shell, e.Id))).Also(l => { l.TextWrapping = TextWrapping.Wrap; l.HorizontalAlignment = HorizontalAlignment.Left; }));
            return cell;
        }));
        if (showVendor) cols.Add(new("Category", Ui.Star(), e => Ui.Muted(Ui.Dash(e.CategoryName), 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap)));
        cols.Add(new("Amount", Ui.Auto, e => Ui.Text(Ui.Money(e.Amount), "Money"), HorizontalAlignment.Right));
        cols.Add(new("Balance", Ui.Auto, e => e.BalanceDue > 0
            ? Ui.Text(Ui.Money(e.BalanceDue), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText")
            : Ui.Muted("—"), HorizontalAlignment.Right));
        cols.Add(new("Status", Ui.Auto, e => StatusBadge(e, today), HorizontalAlignment.Center));
        cols.Add(new("", Ui.Auto, e =>
        {
            var actions = Ui.Row(4,
                Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => shell.Navigate(Routes.Expense(shell, e.Id))),
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => shell.Navigate(Routes.EditExpense(shell, e.Id))));
            if (e.BalanceDue > 0) actions.Children.Add(Ui.IconButton("cash-coin", "Btn.OutlineSuccess", "Record Payment", () => PaymentDialog.Open(shell, e)).Margin(4, 0, 0, 0));
            return actions;
        }, HorizontalAlignment.Right));
        return cols;
    }

    /// <summary>A small, non-clickable money tile, as on the customer page.</summary>
    public static StatTile Small(string value, string label, string tone, double size = 22.4) => new()
    {
        Value = value, Label = label, Tone = tone, Glyph = "", ValueSize = size, Cursor = System.Windows.Input.Cursors.Arrow, Focusable = false,
    };
}
