using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.6. The Banking dialogs: an account, money in or out, a transfer or payout, and a payout's breakdown.</summary>
public static class BankDialogs
{
    private sealed record Item(string Value, string Label) { public override string ToString() => Label; }
    private sealed record AccountItem(long Id, string Label) { public override string ToString() => Label; }

    private static ComboBox Combo(IEnumerable<(string Value, string Label)> items, string? selected)
    {
        var list = items.Select(i => new Item(i.Value, i.Label)).ToList();
        return new ComboBox { ItemsSource = list, SelectedIndex = Math.Max(0, list.FindIndex(i => i.Value == selected)) };
    }

    private static string Value(ComboBox c) => ((Item)c.SelectedItem).Value;

    private static bool Show(Dictionary<string, Field> fields, ValidationException v)
    {
        foreach (var (key, message) in v.Fields) (fields.GetValueOrDefault(key) ?? fields.Values.First()).Error = message;
        return false;
    }

    // ------------------------------------------------------------------ account

    public static async void Account(MainWindow shell, BankAccount? existing)
    {
        var name = Ui.TextBox(existing?.Name, "e.g. Business Checking, Stripe");
        var kind = Combo(AccountKind.All, existing?.Kind ?? AccountKind.Checking);
        var opening = Ui.TextBox(existing is null ? "0.00" : existing.OpeningBalance.ToString("0.00", CultureInfo.InvariantCulture));
        Input.SetPrefix(opening, "$");
        var date = Ui.DateBox(existing?.OpeningDate ?? new DateOnly(DateTime.Today.Year, 1, 1));
        var notes = Ui.TextArea(existing?.Notes, 50);
        var fields = new Dictionary<string, Field>
        {
            ["name"] = Ui.Field("Account Name", name, required: true),
            ["kind"] = Ui.Field("Kind", kind, required: true),
            ["opening_balance"] = Ui.Field("Opening Balance", opening, hint: "What the account held on the opening date (negative for a card balance owed)."),
            ["opening_date"] = Ui.Field("Opening Date", date, required: true),
            ["notes"] = Ui.Field("Notes", notes),
        };
        var body = Ui.Stack(0, fields["name"], fields["kind"],
            Ui.Columns(16, (Ui.Star(), fields["opening_balance"]), (Ui.Star(), fields["opening_date"])), fields["notes"]);
        await shell.OpenDialog(existing is null ? "New Account" : "Edit Account", "bank", body, existing is null ? "Add Account" : "Save", () =>
        {
            foreach (var f in fields.Values) f.Error = null;
            if (!Ui.TryParseSigned(opening.Text, out decimal ob)) { fields["opening_balance"].Error = "Enter dollars and cents, e.g. 1250.00."; return false; }
            var draft = new AccountDraft(name.Text, Value(kind), (double)ob, date.Date, notes.Text);
            try
            {
                if (existing is null)
                {
                    var made = shell.Store.Banking.CreateAccount(draft);
                    shell.Navigate(Routes.BankAccount(shell, made.Id), made.Notice);
                }
                else shell.Reload(shell.Store.Banking.UpdateAccount(existing.Id, draft));
                return true;
            }
            catch (ValidationException v) { return Show(fields, v); }
            catch (UserFacingException ex) { fields["name"].Error = ex.Message; return false; }
        }, primaryGlyph: "check-lg", maxWidth: 520);
    }

    // ------------------------------------------------------------------ money in / out

    public static async void Money(MainWindow shell, BankAccount account, bool moneyIn, BankTransaction? existing)
    {
        var kind = Combo(moneyIn ? TxnKind.MoneyIn : TxnKind.MoneyOut, existing?.Kind);
        var date = Ui.DateBox(existing?.Date ?? DateOnly.FromDateTime(DateTime.Today));
        var amount = Ui.TextBox(existing is null ? "" : Math.Abs(existing.Amount).ToString("0.00", CultureInfo.InvariantCulture));
        Input.SetPrefix(amount, "$");
        var desc = Ui.TextBox(existing?.Description, "Optional, e.g. Monthly service fee");
        var reference = Ui.TextBox(existing?.Reference, account.IsProcessor ? "Charge id, e.g. ch_3P..." : "Optional");
        var cleared = new CheckBox { Content = "Already cleared by the bank", IsChecked = existing is null ? true : !existing.IsPending, Margin = new Thickness(0, 4, 0, 12) };
        var asExpense = new CheckBox { Content = "Also record it as an expense (counts on the Profit & Loss)", IsChecked = shell.Store.Expenses.Enabled, Margin = new Thickness(0, 0, 0, 8) };
        var category = ExpenseUi.CategoryPicker(shell.Store, shell.Store.Categories.Active().FirstOrDefault(c => c.Name == "Interest & Bank Fees")?.Id);
        var fields = new Dictionary<string, Field>
        {
            ["kind"] = Ui.Field("What is it?", kind, required: true),
            ["date"] = Ui.Field("Date", date, required: true),
            ["amount"] = Ui.Field("Amount", amount, required: true),
            ["description"] = Ui.Field("Description", desc),
            ["reference"] = Ui.Field("Reference", reference),
            ["category"] = Ui.Field("Expense Category", category),
        };
        var feeBox = Ui.Stack(0, asExpense, fields["category"]);
        var body = Ui.Stack(0, fields["kind"],
            Ui.Columns(16, (Ui.Star(), fields["amount"]), (Ui.Star(), fields["date"])),
            fields["description"], fields["reference"], cleared, feeBox,
            Ui.Muted(moneyIn ? "Not income: customer payments come in through their invoices." : "Owner draws and transfers are not expenses.", 12.8)
                .Also(t => t.TextWrapping = TextWrapping.Wrap));
        void Sync()
        {
            bool fee = !moneyIn && existing is null && Value(kind) == TxnKind.Fee && shell.Store.Expenses.Enabled;
            feeBox.Visibility = fee ? Visibility.Visible : Visibility.Collapsed;
            fields["category"].Visibility = asExpense.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        kind.SelectionChanged += (_, _) => Sync();
        asExpense.Checked += (_, _) => Sync();
        asExpense.Unchecked += (_, _) => Sync();
        Sync();

        string title = existing is not null ? "Edit Transaction" : moneyIn ? $"Money In — {account.Name}" : $"Money Out — {account.Name}";
        await shell.OpenDialog(title, moneyIn ? "box-arrow-in-down" : "box-arrow-up", body, existing is null ? (moneyIn ? "Add Money In" : "Add Money Out") : "Save", () =>
        {
            foreach (var f in fields.Values) f.Error = null;
            double? value = null;
            if (amount.Text.Trim().Length > 0)
            {
                if (!Ui.TryParseCents(amount.Text, out decimal d)) { fields["amount"].Error = Ui.CentsError; return false; }
                value = (double)d;
            }
            long? feeCategory = feeBox.Visibility == Visibility.Visible && asExpense.IsChecked == true ? ExpenseUi.SelectedId(category) : null;
            var draft = new TxnDraft(account.Id, moneyIn, Value(kind), date.Date, desc.Text, value, reference.Text, "", cleared.IsChecked == true, feeCategory);
            try
            {
                shell.Reload(existing is null ? shell.Store.Banking.AddTransaction(draft).Notice : shell.Store.Banking.UpdateTransaction(existing.Id, draft));
                return true;
            }
            catch (ValidationException v) { return Show(fields, v); }
            catch (UserFacingException ex) { fields["amount"].Error = ex.Message; return false; }
        }, moneyIn ? "Btn.Success" : "Btn.Danger", "check-lg", maxWidth: 520);
    }

    // ------------------------------------------------------------------ transfer / payout

    public static async void Transfer(MainWindow shell, BankAccount? from)
    {
        var accounts = shell.Store.Banking.Accounts().Select(a => new AccountItem(a.Id, a.Name)).ToList();
        if (accounts.Count < 2) { shell.ShowError("Add a second account first: a transfer moves money between two of your accounts."); return; }
        var fromBox = new ComboBox { ItemsSource = accounts, SelectedIndex = Math.Max(0, accounts.FindIndex(a => a.Id == from?.Id)) };
        var toBox = new ComboBox { ItemsSource = accounts, SelectedIndex = accounts.FindIndex(a => a.Id != (from?.Id ?? accounts[0].Id)) };
        var date = Ui.DateBox(DateOnly.FromDateTime(DateTime.Today));
        var amount = Ui.TextBox(from is { IsProcessor: true, Balance: > 0 } ? from.Balance.ToString("0.00", CultureInfo.InvariantCulture) : "");
        Input.SetPrefix(amount, "$");
        var reference = Ui.TextBox(null, from?.IsProcessor == true ? "Payout id, e.g. po_1Q..." : "Optional");
        var desc = Ui.TextBox(null, "Optional");
        var arrived = new CheckBox { Content = "It has arrived (cleared). Leave unticked for a payout still on its way.", IsChecked = from?.IsProcessor != true, Margin = new Thickness(0, 4, 0, 8) };
        var fields = new Dictionary<string, Field>
        {
            ["from"] = Ui.Field("From", fromBox, required: true),
            ["to_account"] = Ui.Field("To", toBox, required: true),
            ["amount"] = Ui.Field("Amount", amount, required: true),
            ["date"] = Ui.Field("Date", date, required: true),
            ["reference"] = Ui.Field("Reference", reference),
            ["description"] = Ui.Field("Description", desc),
        };
        var body = Ui.Stack(0,
            Ui.Columns(16, (Ui.Star(), fields["from"]), (Ui.Star(), fields["to_account"])),
            Ui.Columns(16, (Ui.Star(), fields["amount"]), (Ui.Star(), fields["date"])),
            fields["reference"], fields["description"], arrived,
            Ui.Muted("Moving your own money is not income or an expense. A processor payout is a transfer from the processor to your bank.", 12.8)
                .Also(t => t.TextWrapping = TextWrapping.Wrap));
        await shell.OpenDialog(from?.IsProcessor == true ? $"Payout — {from.Name}" : "Transfer", "arrow-left-right", body, "Record Transfer", () =>
        {
            foreach (var f in fields.Values) f.Error = null;
            if (!Ui.TryParseCents(amount.Text, out decimal d)) { fields["amount"].Error = Ui.CentsError; return false; }
            var draft = new TransferDraft(((AccountItem)fromBox.SelectedItem).Id, ((AccountItem?)toBox.SelectedItem)?.Id ?? 0, date.Date, (double)d, desc.Text, reference.Text, arrived.IsChecked == true);
            try
            {
                shell.Reload(shell.Store.Banking.Transfer(draft).Notice);
                return true;
            }
            catch (ValidationException v) { return Show(fields, v); }
            catch (UserFacingException ex) { fields["amount"].Error = ex.Message; return false; }
        }, primaryGlyph: "check-lg", maxWidth: 540);
    }

    // ------------------------------------------------------------------ payout breakdown

    /// <summary>What a processor payout pays out, and any difference. Shown, never fixed.</summary>
    public static async void Bridge(MainWindow shell, long payoutId)
    {
        PayoutBridge b;
        try { b = shell.Store.Banking.Bridge(payoutId); }
        catch (UserFacingException ex) { shell.ShowError(ex.Message); return; }
        var columns = new List<Column<BankTransaction>>
        {
            new("Date", Ui.Auto, t => Ui.Text(Ui.Date(t.Date), "Body", 13.6)),
            new("Item", Ui.Star(), t => Ui.Stack(2, Ui.Text(t.Description, "Body", 13.6), Ui.Muted(string.IsNullOrEmpty(t.Reference) ? t.KindLabel : $"{t.KindLabel} · {t.Reference}", 12))),
            new("Amount", Ui.Auto, t => BankingPage.Money(t.Amount), HorizontalAlignment.Right),
        };
        TextBlock Line(string label, double v, string? brush = null) =>
            Ui.Text($"{label}: {(v < 0 ? "-" : "")}{Ui.Money(Math.Abs(v))}", "Strong", 14.4).Also(t => { if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush); });
        var body = Ui.Stack(0,
            Ui.Muted("Charges and fees in this account since the payout before this one.", 13).Also(t => t.Margin = new Thickness(0, 0, 0, 8)),
            new Border { BorderThickness = new Thickness(1), Child = b.Items.Count == 0 ? Ui.Muted("No charges or fees since the last payout.", 13.6).Margin(12, 12, 12, 12) : Table.Build(columns, b.Items) }
                .WithResource(Border.BorderBrushProperty, "Border"),
            Line("Expected payout", b.Expected).Margin(0, 12, 0, 0),
            Line("Paid out", b.Paid).Margin(0, 4, 0, 0),
            Line("Difference", b.Difference, b.Difference == 0 ? "SuccessText" : "DangerText").Margin(0, 4, 0, 0),
            Ui.Muted(b.Difference == 0
                ? "The payout matches."
                : "Not adjusted for you. Check the processor's report for a fee or refund not entered yet, and add it with Money Out.", 12.8)
                .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 0); }));
        await shell.OpenDialog("Payout Breakdown", "diagram-3", body, "Close", () => true, maxWidth: 600, cancelText: "");
    }
}
