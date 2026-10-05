using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.2. Recurring expenses: rent, subscriptions, insurance. Laid out like the service item list.</summary>
public sealed class RecurringPage : AppPage
{
    public override string NavKey => "expenses";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Expenses", () => Shell.Navigate(Routes.Expenses(Shell))), new Crumb("Recurring") };

    public RecurringPage(MainWindow shell) : base(shell)
    {
        var all = Store.Recurring.List();

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("arrow-repeat", "Recurring Expenses",
            "Each one adds an expense on its day, every month or year, when ProfitDjinn opens.", null,
            Ui.Button("New Recurring Expense", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewRecurring(Shell)))));

        var columns = new List<Column<RecurringExpense>>
        {
            new("Description", Ui.Star(1.4), t =>
            {
                var cell = new StackPanel();
                var name = Ui.Link(t.Description, () => Shell.Navigate(Routes.EditRecurring(Shell, t.Id))).Also(l => l.HorizontalAlignment = HorizontalAlignment.Left);
                if (!t.IsActive) name.Opacity = 0.6;
                cell.Children.Add(name);
                cell.Children.Add(Ui.Muted(string.Join(" · ", new[] { t.Vendor?.Name, t.Category?.Name }.Where(s => !string.IsNullOrEmpty(s))), 12.8));
                return cell;
            }),
            new("Amount", Ui.Auto, t => Ui.Text(Ui.Money(t.Amount), "Money"), HorizontalAlignment.Right),
            new("Schedule", Ui.Star(), t => Ui.Text(t.ScheduleLabel, "Body", 13.6)),
            new("Type", Ui.Auto, t => t.Mode == RecurringMode.Paid ? Ui.Badge("Auto-paid", "info") : Ui.Badge("Bill", "warning"), HorizontalAlignment.Center),
            new("Next", Ui.Auto, t => !t.IsActive
                ? Ui.Badge("Paused", "secondary")
                : Store.Recurring.NextDate(t) is { } next ? Ui.Muted(Ui.Date(next), 13.6) : Ui.Muted("Ended", 13.6), HorizontalAlignment.Center),
            new("", Ui.Auto, t => Ui.Row(4,
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => Shell.Navigate(Routes.EditRecurring(Shell, t.Id))),
                Ui.IconButton(t.IsActive ? "pause-circle" : "play-circle", "Btn.OutlineSecondary", t.IsActive ? "Pause" : "Turn back on",
                    () => Toggle(t)),
                Ui.IconButton("trash", "Btn.OutlineDanger", "Delete", () => Delete(t))), HorizontalAlignment.Right),
        };
        FrameworkElement body = all.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<RecurringExpense>()),
                Ui.Empty("arrow-repeat", "No recurring expenses yet.", "Add rent, a subscription or insurance.", () => Shell.Navigate(Routes.NewRecurring(Shell))))
            : Table.Build(columns, all, onRowClick: t => Shell.Navigate(Routes.EditRecurring(Shell, t.Id)));
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private void Toggle(RecurringExpense t) => Try(() =>
    {
        var notice = Store.Recurring.ToggleActive(t.Id);
        Shell.Reload(notice);
    });

    private async void Delete(RecurringExpense t)
    {
        if (!await Shell.Confirm($"Delete the recurring expense '{t.Description}'? Expenses it already created are kept.", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Recurring.Delete(t.Id)));
    }
}

/// <summary>2.2. New and Edit Recurring Expense.</summary>
public sealed class RecurringFormPage : AppPage
{
    private readonly long? _id;
    private readonly Dictionary<string, Field> _fields = new();
    private readonly RecordPicker _vendor;
    private readonly ComboBox _category, _frequency, _method;
    private readonly TextBox _description, _amount, _day, _notes;
    private readonly DateBox _start, _end;
    private readonly RadioButton _modePaid, _modeBill;
    private readonly CheckBox _active;
    private readonly Dictionary<long, long?> _vendorDefaults;

    public override string NavKey => "expenses";
    public override IReadOnlyList<Crumb> Crumbs => new[]
    {
        new Crumb("Expenses", () => Shell.Navigate(Routes.Expenses(Shell))),
        new Crumb("Recurring", () => Shell.Navigate(Routes.Recurring(Shell))),
        new Crumb(_id is null ? "New" : "Edit"),
    };

    private sealed record Freq(string Value, string Label) { public override string ToString() => Label; }

    public RecurringFormPage(MainWindow shell, long? id) : base(shell)
    {
        _id = id;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var t = id is { } rid ? Store.Recurring.Get(rid) : new RecurringExpense { StartDate = today, DayOfMonth = today.Day, Mode = RecurringMode.Paid, Method = "credit_card" };
        _vendorDefaults = Store.Vendors.ActiveForPicker().ToDictionary(v => v.Id, v => v.DefaultCategoryId);

        _vendor = QuickAdd.VendorPicker(Shell, t.VendorId, (added, def) => _vendorDefaults![added] = def);
        _category = ExpenseUi.CategoryPicker(Store, id is null ? null : t.CategoryId);
        _description = Ui.TextBox(t.Description, "e.g. Office rent, Adobe subscription");
        _amount = Ui.TextBox(id is null ? "" : t.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        Input.SetPrefix(_amount, "$");
        var freqs = new List<Freq> { new(RecurringFrequency.Monthly, "Every month"), new(RecurringFrequency.Yearly, "Every year") };
        _frequency = new ComboBox { ItemsSource = freqs, SelectedIndex = t.Frequency == RecurringFrequency.Yearly ? 1 : 0 };
        _start = Ui.DateBox(t.StartDate);
        _day = Ui.TextBox(t.DayOfMonth.ToString(CultureInfo.InvariantCulture)).Also(b => { b.MaxLength = 2; b.Width = 80; b.HorizontalAlignment = HorizontalAlignment.Left; });
        _end = Ui.DateBox(t.EndDate);
        _modePaid = new RadioButton { Content = "Auto-paid: it's charged automatically, so record it as paid", GroupName = "mode", IsChecked = t.Mode == RecurringMode.Paid, Margin = new Thickness(0, 0, 0, 6) };
        _modeBill = new RadioButton { Content = "Bill: add it as owed, due that day, and I'll record the payment", GroupName = "mode", IsChecked = t.Mode == RecurringMode.Bill };
        _method = ExpenseUi.MethodPicker(t.Method ?? "credit_card");
        _notes = Ui.TextArea(t.Notes, 60);
        _active = new CheckBox { Content = "Active", IsChecked = t.IsActive };

        _vendor.SelectionChanged += () =>
        {
            if (_vendor.SelectedId is { } v && _vendorDefaults.GetValueOrDefault(v) is { } def)
            {
                int at = ((List<Choice>)_category.ItemsSource).FindIndex(c => c.Id == def);
                if (at >= 0) _category.SelectedIndex = at;
            }
        };
        _start.Changed += () => { if (_id is null && _start.Date is { } d) _day.Text = d.Day.ToString(CultureInfo.InvariantCulture); };

        var form = new StackPanel();
        form.Children.Add(Ui.Columns(16, (Ui.Star(), F("vendor_id", "Vendor", _vendor)), (Ui.Star(), F("category_id", "Category", _category, required: true))));
        form.Children.Add(Ui.Columns(16, (Ui.Star(2), F("description", "Description", _description, required: true)), (Ui.Star(), F("amount", "Amount", _amount, required: true))));
        form.Children.Add(Ui.Columns(16,
            (Ui.Star(), F("frequency", "Repeats", _frequency, required: true)),
            (Ui.Star(), F("start_date", "Starting", _start, required: true)),
            (Ui.Star(), F("day_of_month", "On Day", _day, required: true).Also(f => f.Hint = "Shorter months use their last day.")),
            (Ui.Star(), F("end_date", "Ending", _end).Also(f => f.Hint = "Optional."))));
        var methodField = F("method", "Paid With", _method);
        form.Children.Add(F("mode", "How It's Added", Ui.Stack(0, _modePaid, _modeBill)));
        form.Children.Add(methodField);
        form.Children.Add(F("notes", "Notes", _notes));
        form.Children.Add(_active);
        void Sync() => methodField.Visibility = _modePaid.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _modePaid.Checked += (_, _) => Sync();
        _modeBill.Checked += (_, _) => Sync();
        Sync();

        form.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 24, 0, 24) });
        form.Children.Add(Ui.Row(8,
            Ui.Button("Save Recurring Expense", "Btn.Primary", "check-lg", Save),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Routes.Recurring(Shell)))));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Recurring Expense" : "Edit Recurring Expense",
            id is null ? null : Store.Recurring.NextDate(t) is { } next && t.IsActive ? $"Next one: {Ui.Date(next)}" : null));
        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(4) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        var card = Ui.Card(form);
        Grid.SetColumn(card, 1);
        holder.Children.Add(card);
        page.Children.Add(holder);
        Content = page;

        foreach (var box in new[] { _description, _amount, _day })
            box.KeyDown += (_, k) => { if (k.Key == Key.Enter) Save(); };
    }

    public override void OnShown() => _description.Focus();

    private Field F(string key, string label, UIElement input, bool required = false) => _fields[key] = Ui.Field(label, input, required);

    private async void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox tb) Input.SetInvalid(tb, false); }
        _vendor.Invalid = false;
        if (ExpenseUi.VendorProblem(_vendor) is { } vendorProblem) { ShowFieldErrors(new Dictionary<string, string> { ["vendor_id"] = vendorProblem }); Shell.ShowError(vendorProblem); return; }
        var bad = new Dictionary<string, string>();
        double? amount = null;
        if (_amount.Text.Trim().Length > 0)
        {
            if (Ui.TryParseCents(_amount.Text, out decimal dec)) amount = (double)dec;
            else bad["amount"] = Ui.CentsError;
        }
        int? day = null;
        if (_day.Text.Trim().Length > 0)
        {
            if (int.TryParse(_day.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int d)) day = d;
            else bad["day_of_month"] = "Enter a day from 1 to 31.";
        }
        if (!_start.IsBlank && _start.Date is null) bad["start_date"] = "Enter a date like 2026-03-01.";
        if (!_end.IsBlank && _end.Date is null) bad["end_date"] = "Enter a date like 2027-03-01.";
        if (bad.Count > 0) { ShowFieldErrors(bad); Shell.ShowError(string.Join(" ", bad.Values.Distinct())); return; }

        var draft = new RecurringDraft(_vendor.SelectedId, ExpenseUi.SelectedId(_category), _description.Text, amount,
            ((Freq)_frequency.SelectedItem).Value, _start.Date, day, _end.Date,
            _modePaid.IsChecked == true ? RecurringMode.Paid : RecurringMode.Bill, ExpenseUi.SelectedMethod(_method), _notes.Text, _active.IsChecked == true);

        int past = 0;
        if (!Try(() => past = Store.Recurring.PreviewCount(draft, _id))) return;
        if (past > 0)
        {
            string word = past == 1 ? "expense" : "expenses";
            bool go = await Shell.Confirm(
                $"The start date is in the past, so saving this adds {past} {word} now, one for each date through today. Continue?",
                $"Add {past} {(past == 1 ? "Expense" : "Expenses")}", title: "Add past expenses?");
            if (!go) return;
        }
        Try(() =>
        {
            Notice notice = _id is { } id ? Store.Recurring.Update(id, draft) : Store.Recurring.Create(draft).Notice;
            var made = Store.Recurring.GenerateDue();
            Shell.Navigate(Routes.Recurring(Shell), RecurringService.Summarize(made) is { } s ? new Notice(notice.Message + " " + s.Message, notice.Kind) : notice);
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, message) in errors)
            if (_fields.TryGetValue(key, out var f))
            {
                f.Error = message;
                if (f.Content is TextBox t) Input.SetInvalid(t, true);
                if (f.Content is Controls.RecordPicker rp) rp.Invalid = true;
            }
    }
}
