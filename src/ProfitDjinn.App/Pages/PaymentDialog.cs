using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// Record Payment (1.x's payment dialog on the invoice page and the invoice list). When the
/// customer has unused account credit, "Account Credit" is offered; it applies the credit
/// to this invoice instead of recording money, capped at the credit and the balance.
/// </summary>
public static class PaymentDialog
{
    private sealed record MethodChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public static async void Open(MainWindow shell, Invoice invoice)
    {
        var store = shell.Store;
        double balance = invoice.BalanceDue;
        double credit = store.Invoices.AvailableCredit(invoice.CustomerId);

        var body = new StackPanel();
        var balanceLine = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        balanceLine.Children.Add(Ui.Muted("Balance due: ", 14.4));
        balanceLine.Children.Add(Ui.Text(Ui.Money(balance), "Strong", 14.4).WithResource(TextBlock.ForegroundProperty, "DangerText"));
        body.Children.Add(balanceLine);

        if (credit > 0)
        {
            var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.6 }.WithResource(TextBlock.ForegroundProperty, "Alert.Warning.Fg");
            note.Inlines.Add(new System.Windows.Documents.Run("This customer has "));
            note.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(Ui.Money(credit))));
            note.Inlines.Add(new System.Windows.Documents.Run(" in account credit. Choose "));
            note.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("Account Credit")));
            note.Inlines.Add(new System.Windows.Documents.Run(" below to apply it."));
            var alert = new Border { Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 16), BorderThickness = new Thickness(1), Child = Ui.Row(6, new Icon { Glyph = "wallet2", Size = 14 }.WithResource(Icon.ForegroundProperty, "Alert.Warning.Fg"), note) }
                .WithResource(Border.BackgroundProperty, "Alert.Warning.Bg").WithResource(Border.BorderBrushProperty, "Alert.Warning.Border").WithResource(Border.CornerRadiusProperty, "Radius");
            body.Children.Add(alert);
        }

        var methods = PaymentMethods.All.Select(m => new MethodChoice(m.Value, m.Label)).ToList();
        if (credit > 0) methods.Add(new MethodChoice(PaymentMethods.AccountCredit, $"Account Credit ({Ui.Money(credit)} available)"));
        var method = new ComboBox { ItemsSource = methods, SelectedIndex = 0 };
        var amount = Ui.TextBox(PyMath.Round(balance, 2).ToString("F2", CultureInfo.InvariantCulture));
        Input.SetPrefix(amount, "$");
        var check = Ui.TextBox(null, "e.g. 1042").Also(t => { t.MaxLength = 50; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); });
        var date = Ui.DateBox(DateOnly.FromDateTime(DateTime.Today));   // local date (1.x's list dialog used UTC)
        var notes = Ui.TextArea(null, 56, "Optional notes about this payment…");

        var amountField = Ui.Field("Amount", amount, required: true);
        var checkField = Ui.Field("Check Number", check);
        var dateField = Ui.Field("Date", date, required: true);
        var creditHint = "Applies account credit against this invoice — not recorded as a money payment.";
        body.Children.Add(Ui.Field("Payment Method", method, required: true));
        body.Children.Add(amountField);
        body.Children.Add(checkField);
        body.Children.Add(dateField);
        body.Children.Add(Ui.Field("Notes", notes).Margin(0, 0, 0, 4));
        checkField.Visibility = Visibility.Collapsed;

        double creditCap = Math.Min(credit, balance);
        method.SelectionChanged += (_, _) =>
        {
            string m = ((MethodChoice)method.SelectedItem).Value;
            checkField.Visibility = m == PaymentMethods.Check ? Visibility.Visible : Visibility.Collapsed;
            amountField.Hint = m == PaymentMethods.AccountCredit ? creditHint : null;
            if (m == PaymentMethods.AccountCredit)
            {
                bool ok = double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a);
                if (!ok || a > creditCap) amount.Text = PyMath.Round(creditCap, 2).ToString("F2", CultureInfo.InvariantCulture);
            }
        };

        var title = $"Record Payment — {invoice.InvoiceNumber}";
        bool done = await shell.OpenDialog(title, "cash-coin", body, "Record Payment", () =>
        {
            amountField.Error = checkField.Error = dateField.Error = null;
            string m = ((MethodChoice)method.SelectedItem).Value;
            // The browser's number box only took cents (step 0.01, min 0.01).
            if (!decimal.TryParse(amount.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dec) || dec < 0.01m || decimal.Round(dec, 2) != dec)
            {
                amountField.Error = "Enter an amount in dollars and cents, at least $0.01.";
                return false;
            }
            if (m == PaymentMethods.AccountCredit && (double)dec > PyMath.Round(creditCap, 2) + 1e-9)
            {
                amountField.Error = $"At most {Ui.Money(creditCap)} of credit can be applied here.";
                return false;
            }
            if (m == PaymentMethods.Check && check.Text.Trim().Length == 0)
            {
                checkField.Error = "Enter the check number.";
                return false;
            }
            if (date.Date is null)
            {
                dateField.Error = "Enter the payment date.";
                return false;
            }
            try
            {
                var notice = store.Invoices.RecordPayment(invoice.Id, (double)dec, m, check.Text, date.Date, notes.Text);
                shell.Reload(notice);
                return true;
            }
            catch (UserFacingException ex)
            {
                amountField.Error = ex.Message;
                return false;
            }
        }, "Btn.Success", "check-circle");
        _ = done;
    }
}
