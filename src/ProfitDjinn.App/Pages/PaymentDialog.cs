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
/// Record Payment, for an invoice (1.x's payment dialog) or, in 2.2, an expense. For an
/// invoice whose customer has unused account credit, "Account Credit" is offered; it applies
/// the credit instead of recording money, capped at the credit and the balance. An expense
/// payment is capped at its balance.
/// </summary>
public static class PaymentDialog
{
    private sealed record MethodChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>What is being paid and how to record it.</summary>
    private sealed record Target(
        string Title,
        double Balance,
        double Credit,
        bool CapAtBalance,
        string? CustomerNote,
        bool IsExpense,
        Func<double, string, string?, DateOnly?, string?, PaymentSource, Notice> Record);

    public static void Open(MainWindow shell, Invoice invoice)
    {
        double credit = shell.Store.Invoices.AvailableCredit(invoice.CustomerId);
        Show(shell, new Target($"Record Payment — {invoice.InvoiceNumber}", invoice.BalanceDue, credit, CapAtBalance: false,
            CustomerNote: "This customer has ", IsExpense: false,
            (amount, method, check, date, notes, source) => shell.Store.Invoices.RecordPayment(invoice.Id, amount, method, check, date, notes, source.AccountId)));
    }

    /// <summary>2.2: pay an expense, all or part of its balance.</summary>
    public static void Open(MainWindow shell, Expense expense)
    {
        string name = string.IsNullOrEmpty(expense.VendorName) ? expense.Description : expense.VendorName;
        Show(shell, new Target($"Record Payment — {name}", expense.BalanceDue, 0, CapAtBalance: true, CustomerNote: null, IsExpense: true,
            (amount, method, check, date, notes, source) => shell.Store.Expenses.RecordPayment(expense.Id, amount, method, check, date, notes, source.PaidFrom, source.AccountId)));
    }

    private static async void Show(MainWindow shell, Target target)
    {
        double balance = target.Balance;
        double credit = target.Credit;

        var body = new StackPanel();
        var balanceLine = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        balanceLine.Children.Add(Ui.Muted("Balance due: ", 14.4));
        balanceLine.Children.Add(Ui.Text(Ui.Money(balance), "Strong", 14.4).WithResource(TextBlock.ForegroundProperty, "DangerText"));
        body.Children.Add(balanceLine);

        if (credit > 0)
        {
            var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.6 }.WithResource(TextBlock.ForegroundProperty, "Alert.Warning.Fg");
            note.Inlines.Add(new System.Windows.Documents.Run(target.CustomerNote));
            note.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(Ui.Money(credit))));
            note.Inlines.Add(new System.Windows.Documents.Run(" in account credit. Choose "));
            note.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("Account Credit")));
            note.Inlines.Add(new System.Windows.Documents.Run(" below to apply it."));
            // Icon docked left so the note wraps; in a horizontal row it ran off the dialog's edge.
            var icon = new Icon { Glyph = "wallet2", Size = 14, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top }.WithResource(Icon.ForegroundProperty, "Alert.Warning.Fg");
            DockPanel.SetDock(icon, Dock.Left);
            var line = new DockPanel();
            line.Children.Add(icon);
            line.Children.Add(note);
            var alert = new Border { Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 16), BorderThickness = new Thickness(1), Child = line }
                .WithResource(Border.BackgroundProperty, "Alert.Warning.Bg").WithResource(Border.BorderBrushProperty, "Alert.Warning.Border").WithResource(Border.CornerRadiusProperty, "Radius");
            body.Children.Add(alert);
        }

        var methods = PaymentMethods.All.Select(m => new MethodChoice(m.Value, m.Label)).ToList();
        if (credit > 0) methods.Add(new MethodChoice(PaymentMethods.AccountCredit, $"Account Credit ({Ui.Money(credit)} available)"));
        var method = new Dropdown { ItemsSource = methods, SelectedIndex = 0 };
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
        // 2.6: an expense says who paid it; with Bank Accounts on, either says which account.
        ComboBox? source = target.IsExpense ? ExpenseUi.PaidFromPicker(shell.Store) : ExpenseUi.DepositPicker(shell.Store);
        Field? sourceField = source is null ? null
            : Ui.Field(target.IsExpense ? "Paid From" : "Deposited To", source,
                hint: target.IsExpense ? null : "Optional. Records the money arriving in that account on the Banking page.");
        if (sourceField is not null) body.Children.Add(sourceField);
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
            // Applying account credit moves no money, so there is no account to deposit to.
            if (sourceField is not null && !target.IsExpense) sourceField.Visibility = m == PaymentMethods.AccountCredit ? Visibility.Collapsed : Visibility.Visible;
            if (m == PaymentMethods.AccountCredit)
            {
                bool ok = double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a);
                if (!ok || a > creditCap) amount.Text = PyMath.Round(creditCap, 2).ToString("F2", CultureInfo.InvariantCulture);
            }
        };

        bool done = await shell.OpenDialog(target.Title, "cash-coin", body, "Record Payment", () =>
        {
            amountField.Error = checkField.Error = dateField.Error = null;
            string m = ((MethodChoice)method.SelectedItem).Value;
            if (!Ui.TryParseCents(amount.Text, out decimal dec))
            {
                amountField.Error = Ui.CentsError;
                return false;
            }
            if (m == PaymentMethods.AccountCredit && (double)dec > PyMath.Round(creditCap, 2) + 1e-9)
            {
                amountField.Error = $"At most {Ui.Money(creditCap)} of credit can be applied here.";
                return false;
            }
            if (target.CapAtBalance && (double)dec > PyMath.Round(balance, 2) + 1e-9)
            {
                amountField.Error = $"At most {Ui.Money(balance)} is due.";
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
                var notice = target.Record((double)dec, m, check.Text, date.Date, notes.Text, ExpenseUi.SourceOf(source, m));
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
