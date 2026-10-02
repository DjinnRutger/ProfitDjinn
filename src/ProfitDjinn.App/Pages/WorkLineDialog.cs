using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// The work dialog on a customer's tab (1.x work_orders/detail.html). One dialog, three uses:
/// Log Work (new finished work), Complete (a to-do becomes work), and Edit.
/// </summary>
public static class WorkLineDialog
{
    public enum Mode { Log, Complete, Edit }

    private sealed record TypeChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Opens the dialog. For Log, <paramref name="line"/> is null and the quick-add text carries over.</summary>
    public static void Open(MainWindow shell, WorkOrder wo, Mode mode, WorkOrderLine? line, string quickDescription = "", string quickLabel = "")
    {
        var store = shell.Store;
        double defaultRate = store.WorkOrders.DefaultHourlyRate();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Starting values, as 1.x's dialog filled them.
        string desc, label, type, qty, rate, note;
        DateOnly? date;
        bool noCharge;
        switch (mode)
        {
            case Mode.Log:
                desc = quickDescription; label = quickLabel; type = LineTypes.Labor; date = today;
                qty = "1"; rate = PyMath.JsToFixedText(defaultRate, 2); noCharge = false; note = "";
                break;
            case Mode.Complete:
                desc = line!.Description; label = line.ProjectLabel ?? ""; type = line.LineType; date = today;
                qty = InvoiceDetailPage.QtyText(line.Quantity);
                rate = PyMath.JsToFixedText(line.Rate != 0 ? line.Rate : defaultRate, 2);
                noCharge = line.NoCharge; note = line.InternalNote ?? "";
                break;
            default:
                desc = line!.Description; label = line.ProjectLabel ?? ""; type = line.LineType; date = line.DatePerformed;
                qty = InvoiceDetailPage.QtyText(line.Quantity); rate = PyMath.JsToFixedText(line.Rate, 2);
                noCharge = line.NoCharge; note = line.InternalNote ?? "";
                break;
        }

        var body = new StackPanel();
        var items = store.Items.Active();
        var descBox = Ui.TextBox(desc);
        var qtyBox = Ui.TextBox(qty);
        var rateBox = Ui.TextBox(rate);
        Input.SetPrefix(rateBox, "$");
        var typeBox = new ComboBox { ItemsSource = LineTypes.All.Select(t => new TypeChoice(t, LineTypes.Label(t))).ToList() };
        typeBox.SelectedIndex = Math.Max(0, LineTypes.All.ToList().IndexOf(LineTypes.IsValid(type) ? type : LineTypes.Labor));

        if (items.Count > 0)
        {
            var picker = new ComboBox { ItemsSource = items.Select(i => $"{i.Description} — {Ui.Money(i.Price)}").ToList() };
            Input.SetPlaceholder(picker, "Pick a service item to fill in the line…");
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var item = items[picker.SelectedIndex];
                descBox.Text = item.Description;
                typeBox.SelectedIndex = LineTypes.All.ToList().IndexOf(LineTypes.Service);
                qtyBox.Text = "1";
                rateBox.Text = PyMath.JsToFixedText(item.Price, 2);
                picker.SelectedIndex = -1;
            };
            body.Children.Add(Ui.Field("Quick-pick from Items", picker));
        }

        var labelBox = new SuggestBox(wo.OpenLabels, label, "Optional");
        var dateBox = Ui.DateBox(date);
        var noChargeBox = new CheckBox { Content = "No charge", IsChecked = noCharge };
        var noteBox = Ui.TextArea(note, 56, "Private — never appears on an invoice");
        var descField = Ui.Field("Description", descBox, required: true);
        var dateField = Ui.Field("Date", dateBox, hint: mode == Mode.Edit && line!.Status == "pending" ? "Leave blank to keep this a to-do." : null);
        var qtyField = Ui.Field("Hours", qtyBox);
        var rateField = Ui.Field("Rate ($/hr)", rateBox);
        var total = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 15 }.WithResource(TextBlock.ForegroundProperty, "Text");

        body.Children.Add(descField);
        body.Children.Add(Ui.Columns(16, (Ui.Star(), Ui.Field("Project", labelBox)), (Ui.Star(), Ui.Field("Type", typeBox))));
        body.Children.Add(Ui.Columns(16, (Ui.Star(), dateField), (Ui.Star(), qtyField), (Ui.Star(), rateField)));
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(total, Dock.Right);
        totalRow.Children.Add(total);
        totalRow.Children.Add(Ui.Muted("Line total", 14));
        body.Children.Add(totalRow);
        body.Children.Add(noChargeBox.Margin(0, 0, 0, 12));
        body.Children.Add(Ui.Field("Internal note", noteBox).Margin(0, 0, 0, 0));

        void Refresh()
        {
            bool labor = (typeBox.SelectedItem as TypeChoice)?.Value == LineTypes.Labor;
            qtyField.Label = labor ? "Hours" : "Quantity";
            rateField.Label = labor ? "Rate ($/hr)" : "Unit Price ($)";
            double q = InvoiceRows.JsParseFloat(qtyBox.Text), r = InvoiceRows.JsParseFloat(rateBox.Text);
            if (double.IsNaN(q)) q = 0;
            if (double.IsNaN(r)) r = 0;
            total.Text = Ui.MoneyGrouped(q * r);
            bool nc = noChargeBox.IsChecked == true;
            total.TextDecorations = nc ? TextDecorations.Strikethrough : null;
            total.SetResourceReference(TextBlock.ForegroundProperty, nc ? "TextMuted" : "Text");
        }
        typeBox.SelectionChanged += (_, _) => Refresh();
        qtyBox.TextChanged += (_, _) => Refresh();
        rateBox.TextChanged += (_, _) => Refresh();
        noChargeBox.Click += (_, _) => Refresh();
        Refresh();

        string title = mode switch { Mode.Log => "Log Work", Mode.Complete => $"Complete: {desc}", _ => "Edit line" };
        string submit = mode switch { Mode.Log => "Log Work", Mode.Complete => "Mark Complete", _ => "Save Changes" };
        _ = shell.OpenDialog(title, "clock-history", body, submit, () =>
        {
            descField.Error = dateField.Error = null;
            if (descBox.Text.Trim().Length == 0) { descField.Error = "A description is required."; return false; }
            if (!dateBox.IsBlank && dateBox.Date is null) { dateField.Error = "Not a valid date value."; return false; }
            var input = new LineInput(descBox.Text, labelBox.Text, (typeBox.SelectedItem as TypeChoice)?.Value ?? LineTypes.Labor,
                dateBox.Date, qtyBox.Text.Length == 0 ? "1" : qtyBox.Text, rateBox.Text, noChargeBox.IsChecked == true, noteBox.Text);
            try
            {
                var notice = mode switch
                {
                    Mode.Log => store.WorkOrders.LogWork(wo.Id, input),
                    Mode.Complete => store.WorkOrders.CompleteLine(line!.Id, input),
                    _ => store.WorkOrders.EditLine(line!.Id, input),
                };
                shell.Reload(notice);
                return true;
            }
            catch (UserFacingException ex)
            {
                descField.Error = ex.Message;
                return false;
            }
        }, "Btn.Primary", "check-lg", maxWidth: 560);
    }
}
