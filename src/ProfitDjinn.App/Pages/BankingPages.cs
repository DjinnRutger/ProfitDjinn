using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.6. Banking: every account with its balance, what has cleared, and what is still pending.</summary>
public sealed class BankingPage : AppPage
{
    public override string NavKey => "banking";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Banking") };

    public BankingPage(MainWindow shell) : base(shell)
    {
        var accounts = Store.Banking.Accounts(includeInactive: true);
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("bank", "Banking",
            "Your bank, card and payment-processor accounts: what is in them, and what is still waiting to clear.", null,
            Ui.Button("Transfer", "Btn.OutlineSecondary", "arrow-left-right", () => BankDialogs.Transfer(Shell, null)),
            Ui.Button("New Account", "Btn.Primary", "plus-lg", () => BankDialogs.Account(Shell, null))));

        var open = accounts.Where(a => a.IsActive).ToList();
        if (open.Count > 0)
        {
            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-8, 0, -8, 24) };
            double cash = PyMath.Sum(open.Where(a => a.Kind is AccountKind.Checking or AccountKind.Savings), a => a.Balance);
            double processors = PyMath.Sum(open.Where(a => a.IsProcessor), a => a.Balance);
            int pending = open.Sum(a => a.PendingCount);
            tiles.Children.Add(Tile(Ui.Money(cash), "In the Bank", "bank", "success", "Checking and savings balances, including pending."));
            tiles.Children.Add(Tile(Ui.Money(processors), "Waiting at Processors", "credit-card", "info", "Money held by Stripe, Square or PayPal, not yet paid out."));
            tiles.Children.Add(Tile(pending.ToString(CultureInfo.InvariantCulture), "Pending", "hourglass-split", "warning", "Transactions not yet cleared by the bank."));
            page.Children.Add(tiles);
        }

        var columns = new List<Column<BankAccount>>
        {
            new("Account", Ui.Star(1.3), a =>
            {
                var cell = Ui.Stack(2, Ui.Link(a.Name, () => Shell.Navigate(Routes.BankAccount(Shell, a.Id))).Also(l => l.HorizontalAlignment = HorizontalAlignment.Left),
                    Ui.Muted(a.IsActive ? a.KindLabel : $"{a.KindLabel} · closed", 12.8));
                if (!a.IsActive) cell.Opacity = 0.6;
                return cell;
            }),
            new("Balance", Ui.Auto, a => Money(a.Balance, bold: true), HorizontalAlignment.Right),
            new("Cleared", Ui.Auto, a => Money(a.ClearedBalance), HorizontalAlignment.Right),
            new("Pending", Ui.Auto, a => a.PendingCount > 0 ? Ui.Badge(a.PendingCount.ToString(CultureInfo.InvariantCulture), "warning") : Ui.Muted("—", 14.4), HorizontalAlignment.Center),
            new("Reconciled", Ui.Auto, a => Ui.Muted(a.ReconciledThrough is { } d ? $"through {Ui.Date(d)}" : "never", 13.6), HorizontalAlignment.Right),
            new("", Ui.Auto, a => Ui.Row(4,
                Ui.IconButton("eye", "Btn.OutlineSecondary", "Open account", () => Shell.Navigate(Routes.BankAccount(Shell, a.Id))),
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit account", () => BankDialogs.Account(Shell, a)),
                Ui.IconButton(a.IsActive ? "archive" : "arrow-counterclockwise", "Btn.OutlineSecondary", a.IsActive ? "Close account" : "Reopen account",
                    () => Try(() => Shell.Reload(Store.Banking.ToggleActive(a.Id)))),
                Ui.IconButton("trash", "Btn.OutlineDanger", "Delete account", () => Delete(a))), HorizontalAlignment.Right),
        };
        page.Children.Add(Ui.Card(accounts.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<BankAccount>()),
                Ui.Empty("bank", "No accounts yet.", "Add your business checking account.", () => BankDialogs.Account(Shell, null)))
            : Table.Build(columns, accounts, onRowClick: a => Shell.Navigate(Routes.BankAccount(Shell, a.Id))), bodyPadding: new Thickness(0)));
        Content = page;
    }

    internal static TextBlock Money(double v, bool bold = false) =>
        Ui.Text((v < 0 ? "-" : "") + Ui.Money(Math.Abs(v)), bold ? "Strong" : "Body", 14.4)
            .Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; if (v < 0) t.SetResourceReference(TextBlock.ForegroundProperty, "DangerText"); });

    internal static StatTile Tile(string value, string label, string glyph, string tone, string tip) => new()
    {
        Value = value, Label = label, Glyph = glyph, Tone = tone, Margin = new Thickness(8, 0, 8, 0), Focusable = false,
        Cursor = System.Windows.Input.Cursors.Arrow, ToolTip = tip,
    };

    private async void Delete(BankAccount a)
    {
        if (!await Shell.Confirm($"Delete the account '{a.Name}'?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Banking.DeleteAccount(a.Id)));
    }
}

/// <summary>2.6. One account's register: every transaction with the running balance after it.</summary>
public sealed class BankAccountPage : AppPage
{
    private readonly BankAccount _a;

    public override string NavKey => "banking";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Banking", () => Shell.Navigate(Routes.Banking(Shell))), new Crumb(_a.Name) };

    public BankAccountPage(MainWindow shell, long id) : base(shell)
    {
        _a = Store.Banking.Account(id);
        var a = _a;
        var rows = Store.Banking.Register(id).Reverse().ToList();     // newest first

        var page = new StackPanel();
        var actions = new List<UIElement>();
        if (a.IsActive)
        {
            actions.Add(Ui.Button("Money In", "Btn.OutlineSuccess", "box-arrow-in-down", () => BankDialogs.Money(Shell, a, moneyIn: true, null)));
            actions.Add(Ui.Button("Money Out", "Btn.OutlineDanger", "box-arrow-up", () => BankDialogs.Money(Shell, a, moneyIn: false, null)));
            actions.Add(Ui.Button(a.IsProcessor ? "Payout" : "Transfer", "Btn.OutlineSecondary", "arrow-left-right", () => BankDialogs.Transfer(Shell, a)));
            actions.Add(Ui.Button("Reconcile", "Btn.Primary", "check2-square", () => Shell.Navigate(Routes.Reconcile(Shell, a.Id))));
        }
        page.Children.Add(Ui.PageHeaderWithGlyph(a.IsProcessor ? "credit-card" : "bank", a.Name,
            a.IsActive ? a.KindLabel : $"{a.KindLabel} · closed", null, actions.ToArray()));

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-8, 0, -8, 24) };
        tiles.Children.Add(BankingPage.Tile(Signed(a.Balance), a.IsProcessor ? "Expected Next Payout" : "Balance", a.IsProcessor ? "credit-card" : "bank", "primary",
            a.IsProcessor ? "Charges and fees not yet paid out to your bank." : "Everything, including pending."));
        tiles.Children.Add(BankingPage.Tile(Signed(a.ClearedBalance), "Cleared", "check-circle", "success", "What the bank has cleared, plus the opening balance."));
        tiles.Children.Add(BankingPage.Tile(a.ReconciledThrough is { } rt ? Ui.Date(rt) : "Never", "Reconciled Through", "check2-square", "info",
            a.ReconciledBalance is { } rb ? $"Statement balance {Ui.Money(rb)}." : "Reconcile against your statement to lock in the past."));
        page.Children.Add(tiles);

        var columns = new List<Column<RegisterRow>>
        {
            new("Date", Ui.Auto, r => Ui.Text(Ui.Date(r.Txn.Date), "Body", 14.4)),
            new("Description", Ui.Star(), r => Describe(r.Txn)),
            new("Reference", Ui.Auto, r => Ui.Muted(string.IsNullOrEmpty(r.Txn.Reference) ? "—" : r.Txn.Reference, 13.6).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"))),
            new("Money In", Ui.Auto, r => r.Txn.Amount > 0 ? Ui.Text(Ui.Money(r.Txn.Amount), "Body", 14.4).WithResource(TextBlock.ForegroundProperty, "SuccessText") : Ui.Muted("", 14.4), HorizontalAlignment.Right),
            new("Money Out", Ui.Auto, r => r.Txn.Amount < 0 ? Ui.Text(Ui.Money(-r.Txn.Amount), "Body", 14.4) : Ui.Muted("", 14.4), HorizontalAlignment.Right),
            new("Balance", Ui.Auto, r => BankingPage.Money(r.Balance, bold: true), HorizontalAlignment.Right),
            new("Status", Ui.Auto, Status, HorizontalAlignment.Center),
            new("", Ui.Auto, Actions, HorizontalAlignment.Right),
        };
        var opening = Ui.Muted($"Opening balance {Signed(a.OpeningBalance)} on {Ui.Date(a.OpeningDate)}.", 13.6).Also(t => t.Margin = new Thickness(16, 12, 16, 12));
        page.Children.Add(Ui.Card(rows.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<RegisterRow>()),
                Ui.Empty("journal", "No transactions yet.", a.IsActive ? "Add money in or out." : null, a.IsActive ? () => BankDialogs.Money(Shell, a, true, null) : null), opening)
            : Ui.Stack(0, Table.Build(columns, rows), opening), "Transactions", "journal-text", bodyPadding: new Thickness(0)));
        Content = page;
    }

    private static string Signed(double v) => (v < 0 ? "-" : "") + Ui.Money(Math.Abs(v));

    private UIElement Describe(BankTransaction t)
    {
        var cell = new StackPanel();
        cell.Children.Add(Ui.Text(t.Description, "Body", 14.4).Also(x => x.TextWrapping = TextWrapping.Wrap));
        var sub = new WrapPanel();
        sub.Children.Add(Ui.Muted(t.KindLabel, 12.8));
        if (t.InvoiceId is { } inv)
            sub.Children.Add(Ui.Link("  · open invoice", () => Shell.Navigate(Routes.Invoice(Shell, inv)), bold: false).Also(l => l.FontSize = 12.8));
        else if (t.ExpenseId is { } exp)
            sub.Children.Add(Ui.Link("  · open expense", () => Shell.Navigate(Routes.Expense(Shell, exp)), bold: false).Also(l => l.FontSize = 12.8));
        cell.Children.Add(sub);
        return cell;
    }

    private UIElement Status(RegisterRow r)
    {
        var t = r.Txn;
        if (t.IsReconciled) return Ui.Badge("Reconciled", "success");
        var b = Ui.Button(t.IsPending ? "Pending" : "Cleared", t.IsPending ? "Btn.OutlineWarning" : "Btn.OutlineSuccess", null,
            () => Try(() => Shell.Reload(Store.Banking.ToggleCleared(t.Id))), small: true,
            tooltip: t.IsPending ? "Waiting for the bank. Click once it shows on your statement." : "Cleared by the bank. Click to mark pending again.");
        System.Windows.Automation.AutomationProperties.SetName(b, $"{(t.IsPending ? "Pending" : "Cleared")}: {t.Description}");
        return b;
    }

    private UIElement Actions(RegisterRow r)
    {
        var t = r.Txn;
        var row = Ui.Row(4);
        if (_a.IsProcessor && t.Kind == TxnKind.Transfer && t.Amount < 0)
            row.Children.Add(Ui.IconButton("diagram-3", "Btn.OutlineInfo", "What this payout pays out", () => BankDialogs.Bridge(Shell, t.Id)));
        if (!t.IsReconciled && !t.IsLinked && TxnKind.IsManual(t.Kind))
            row.Children.Add(Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => BankDialogs.Money(Shell, _a, t.Amount > 0, t)));
        if (!t.IsReconciled && !t.IsLinked)
            row.Children.Add(Ui.IconButton("trash", "Btn.OutlineDanger", "Delete", () => Delete(t)));
        return row;
    }

    private async void Delete(BankTransaction t)
    {
        string what = t.TransferId is not null ? "this transfer from both accounts" : $"'{t.Description}'";
        if (!await Shell.Confirm($"Delete {what}?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Banking.DeleteTransaction(t.Id)));
    }
}

/// <summary>
/// 2.6. Reconcile an account to a statement: tick what the statement shows until the difference
/// is zero, then finish. Nothing is adjusted for you.
/// </summary>
public sealed class ReconcilePage : AppPage
{
    private readonly BankAccount _a;
    private readonly DateBox _through;
    private readonly TextBox _statement;
    private readonly StackPanel _list = new();
    private readonly TextBlock _cleared = Ui.Text("", "Strong", 18), _difference = Ui.Text("", "Strong", 18);
    private readonly Button _finish;
    private readonly HashSet<long> _ticked = new();
    private List<BankTransaction> _rows = new();
    private readonly double _start;

    public override string NavKey => "banking";
    public override IReadOnlyList<Crumb> Crumbs => new[]
    {
        new Crumb("Banking", () => Shell.Navigate(Routes.Banking(Shell))),
        new Crumb(_a.Name, () => Shell.Navigate(Routes.BankAccount(Shell, _a.Id))),
        new Crumb("Reconcile"),
    };

    public ReconcilePage(MainWindow shell, long id) : base(shell)
    {
        _a = Store.Banking.Account(id);
        _start = Store.Banking.ReconciledStart(id);
        _through = Ui.DateBox(DateOnly.FromDateTime(DateTime.Today));
        _statement = Ui.TextBox(null, "0.00");
        Input.SetPrefix(_statement, "$");
        _finish = Ui.Button("Finish Reconciling", "Btn.Success", "check-lg", Finish);
        _through.Changed += Load;
        _statement.TextChanged += (_, _) => Update();

        var inputs = Ui.Columns(16,
            (Ui.Star(), Ui.Field("Statement End Date", _through, required: true)),
            (Ui.Star(), Ui.Field("Statement Ending Balance", _statement, required: true, hint: "The closing balance printed on the statement.")),
            (Ui.Star(), Ui.Field("Starting From", Ui.Text(Ui.Money(_start), "Strong", 16), hint: _a.ReconciledThrough is { } rt ? $"Reconciled through {Ui.Date(rt)}." : "The opening balance.")));

        var totals = Ui.Columns(16,
            (Ui.Star(), Ui.Stack(2, Ui.Muted("TICKED BALANCE", 12), _cleared)),
            (Ui.Star(), Ui.Stack(2, Ui.Muted("DIFFERENCE", 12), _difference)),
            (Ui.Star(), _finish.Also(b => { b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Center; })));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("check2-square", $"Reconcile {_a.Name}",
            "Tick each transaction that appears on your statement. When the difference is zero, finish.", null,
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Routes.BankAccount(Shell, _a.Id)))));
        page.Children.Add(Ui.Card(inputs, bodyPadding: new Thickness(16, 16, 16, 4)).Margin(0, 0, 0, 24));
        page.Children.Add(Ui.Card(_list, "On the Statement?", "list-check", bodyPadding: new Thickness(0)).Margin(0, 0, 0, 24));
        page.Children.Add(Ui.Card(totals, bodyPadding: new Thickness(16)));
        Content = page;
        Load();
    }

    public override void OnShown() => _statement.Focus();

    private void Load()
    {
        if (_through.Date is not { } through) return;
        _rows = Store.Banking.Unreconciled(_a.Id, through).ToList();
        _ticked.Clear();
        foreach (var r in _rows.Where(r => !r.IsPending)) _ticked.Add(r.Id);   // cleared rows start ticked
        var columns = new List<Column<BankTransaction>>
        {
            new("", Ui.Px(44), t =>
            {
                var box = new CheckBox { IsChecked = _ticked.Contains(t.Id), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(box, $"On statement: {t.Description}");
                box.Checked += (_, _) => { _ticked.Add(t.Id); Update(); };
                box.Unchecked += (_, _) => { _ticked.Remove(t.Id); Update(); };
                return box;
            }),
            new("Date", Ui.Auto, t => Ui.Text(Ui.Date(t.Date), "Body", 14.4)),
            new("Description", Ui.Star(), t => Ui.Stack(2, Ui.Text(t.Description, "Body", 14.4), Ui.Muted(t.KindLabel, 12.8))),
            new("Money In", Ui.Auto, t => t.Amount > 0 ? Ui.Text(Ui.Money(t.Amount), "Body", 14.4) : Ui.Muted("", 14.4), HorizontalAlignment.Right),
            new("Money Out", Ui.Auto, t => t.Amount < 0 ? Ui.Text(Ui.Money(-t.Amount), "Body", 14.4) : Ui.Muted("", 14.4), HorizontalAlignment.Right),
        };
        _list.Children.Clear();
        _list.Children.Add(_rows.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<BankTransaction>()), Ui.Empty("check2-all", "Nothing left to reconcile up to this date."))
            : Table.Build(columns, _rows));
        Update();
    }

    private double Ticked => PyMath.Round(_start + PyMath.Sum(_rows.Where(r => _ticked.Contains(r.Id)), r => r.Amount), 2);

    private void Update()
    {
        _cleared.Text = Ui.Money(Ticked);
        bool ok = Ui.TryParseSigned(_statement.Text, out decimal statement);
        double diff = ok ? PyMath.Round((double)statement - Ticked, 2) : double.NaN;
        _difference.Text = ok ? (diff < 0 ? "-" : "") + Ui.Money(Math.Abs(diff)) : "—";
        _difference.SetResourceReference(TextBlock.ForegroundProperty, ok && diff == 0 ? "SuccessText" : "DangerText");
        _finish.IsEnabled = ok && diff == 0 && _through.Date is not null;
    }

    private void Finish()
    {
        if (_through.Date is not { } through || !Ui.TryParseSigned(_statement.Text, out decimal statement)) return;
        Try(() => Shell.Navigate(Routes.BankAccount(Shell, _a.Id), Store.Banking.FinishReconcile(_a.Id, through, (double)statement, _ticked.ToList())));
    }
}
