using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.4. Every recurring invoice, from the Invoices page. Laid out like Recurring Expenses.</summary>
public sealed class RecurringInvoicesPage : AppPage
{
    public override string NavKey => "invoices";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Invoices", () => Shell.Navigate(Routes.Invoices(Shell))), new Crumb("Recurring") };

    public RecurringInvoicesPage(MainWindow shell) : base(shell)
    {
        var all = Store.RecurringInvoices.List();
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("arrow-repeat", "Recurring Invoices",
            "Each one creates an invoice on its day, every month or year, when ProfitDjinn opens.", null,
            Ui.Button("New Recurring Invoice", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewRecurringInvoice(Shell)))));
        page.Children.Add(Ui.Card(all.Count == 0
            ? Ui.Stack(0, Table(Shell, all, showCustomer: true),
                Ui.Empty("arrow-repeat", "No recurring invoices yet.", "Set one up.", () => Shell.Navigate(Routes.NewRecurringInvoice(Shell))))
            : Table(Shell, all, showCustomer: true), bodyPadding: new Thickness(0)));
        Content = page;
    }

    /// <summary>The schedule table, shared with the customer page (which leaves out the customer column).</summary>
    public static FrameworkElement Table(MainWindow shell, IReadOnlyList<RecurringInvoice> schedules, bool showCustomer)
    {
        var columns = new List<Column<RecurringInvoice>>();
        if (showCustomer)
            columns.Add(new("Customer", Ui.Star(1.2), t => Ui.Link(t.Customer?.Name ?? "(deleted customer)", () => shell.Navigate(Routes.Customer(shell, t.CustomerId)))
                .Also(l => { l.HorizontalAlignment = HorizontalAlignment.Left; if (!t.IsActive) l.Opacity = 0.6; })));
        columns.Add(new("Invoice", Ui.Star(1.4), t =>
        {
            // Placeholders shown as the next invoice will read.
            var when = t.NextDate ?? DateOnly.FromDateTime(DateTime.Today);
            string first = PeriodText.Fill(t.Lines.FirstOrDefault()?.Description, when);
            var cell = new StackPanel();
            cell.Children.Add(Ui.Text(first, "Body", 14.4).Also(x => { x.TextTrimming = TextTrimming.CharacterEllipsis; if (!t.IsActive) x.Opacity = 0.6; }));
            if (t.Lines.Count > 1) cell.Children.Add(Ui.Muted($"+ {t.Lines.Count - 1} more {Plural(t.Lines.Count - 1, "line")}", 12.8));
            return cell;
        }));
        columns.Add(new("Amount", Ui.Auto, t => Ui.Text(Ui.Money(t.Total), "Money"), HorizontalAlignment.Right));
        columns.Add(new("Schedule", Ui.Star(), t => Ui.Text(t.ScheduleLabel, "Body", 13.6)));
        columns.Add(new("Next", Ui.Auto, t => !t.IsActive
            ? Ui.Badge("Paused", "secondary")
            : t.NextDate is { } next ? Ui.Muted(Ui.Date(next), 13.6) : Ui.Muted("Ended", 13.6), HorizontalAlignment.Center));
        columns.Add(new("", Ui.Auto, t =>
        {
            var row = Ui.Row(4);
            if (t.IsActive && t.NextDate is not null)
                row.Children.Add(Ui.IconButton("eye", "Btn.OutlineSecondary", "View the next invoice", () => shell.Navigate(Routes.Upcoming(shell, t.Id))));
            row.Children.Add(Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit schedule", () => shell.Navigate(Routes.EditRecurringInvoice(shell, t.Id))));
            row.Children.Add(Ui.IconButton(t.IsActive ? "pause-circle" : "play-circle", "Btn.OutlineSecondary", t.IsActive ? "Pause" : "Turn back on",
                () => Run(shell, () => shell.Reload(shell.Store.RecurringInvoices.ToggleActive(t.Id)))));
            row.Children.Add(Ui.IconButton("trash", "Btn.OutlineDanger", "Delete", async () =>
            {
                if (!await shell.Confirm($"Delete the recurring invoice for {t.Customer?.Name ?? "this customer"}? Invoices it already created are kept.", "Delete", danger: true)) return;
                Run(shell, () => shell.Reload(shell.Store.RecurringInvoices.Delete(t.Id)));
            }));
            return row;
        }, HorizontalAlignment.Right));
        return Controls.Table.Build(columns, schedules, onRowClick: t => shell.Navigate(Routes.EditRecurringInvoice(shell, t.Id)));
    }

    private static void Run(MainWindow shell, Action action)
    {
        try { action(); }
        catch (UserFacingException ex) { shell.ShowError(ex.Message); }
    }

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";
}

/// <summary>2.4. New and Edit Recurring Invoice: the invoice's lines plus when it repeats.</summary>
public sealed class RecurringInvoiceFormPage : AppPage
{
    private sealed record Freq(string Value, string Label) { public override string ToString() => Label; }

    private readonly long? _id;
    private readonly long? _fromCustomer;
    private readonly Dictionary<string, Field> _fields = new();
    private readonly ComboBox _frequency;
    private readonly RecordPicker _customer;
    private readonly DateBox _start, _end;
    private readonly TextBox _day, _notes, _term1, _term2;
    private readonly CheckBox _active;
    private readonly LineBuilder _lines;
    private readonly TextBlock _summaryTotal = new() { FontWeight = FontWeights.Bold, FontSize = 24 };
    private readonly TextBlock _nextText = Ui.Muted("", 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap);

    public override string NavKey => _fromCustomer is null ? "invoices" : "customers";
    public override IReadOnlyList<Crumb> Crumbs => _fromCustomer is { } cid
        ? new[]
        {
            new Crumb("Customers", () => Shell.Navigate(Routes.Customers(Shell))),
            new Crumb(Store.Customers.Get(cid).Name, () => Shell.Navigate(Routes.Customer(Shell, cid))),
            new Crumb("New Recurring Invoice"),
        }
        : new[]
        {
            new Crumb("Invoices", () => Shell.Navigate(Routes.Invoices(Shell))),
            new Crumb("Recurring", () => Shell.Navigate(Routes.RecurringInvoices(Shell))),
            new Crumb(_id is null ? "New" : "Edit"),
        };

    public RecurringInvoiceFormPage(MainWindow shell, long? id, long? customerId) : base(shell)
    {
        _id = id;
        _fromCustomer = id is null ? customerId : null;
        var existing = id is { } rid ? Store.RecurringInvoices.Get(rid) : null;
        var draft = existing is null ? Store.RecurringInvoices.NewDraft(customerId) : null;

        long? selected = existing is not null ? existing.CustomerId : draft!.CustomerId;
        _customer = QuickAdd.CustomerPicker(Shell, selected, existing?.Customer);

        // Fill from the schedule when editing, from the new-schedule defaults otherwise. (2.4 wrote
        // `existing?.X ?? draft!.X`, which reached the null draft whenever a saved value was null,
        // e.g. no end date, and crashed the Edit page.)
        var d = existing is null ? draft! : new RecurringInvoiceDraft(existing.CustomerId, existing.Interval, existing.StartDate, existing.DayOfMonth,
            existing.EndDate, existing.Notes ?? "", existing.Term1 ?? "", existing.Term2 ?? "", existing.IsActive, Array.Empty<InvoiceLineDraft>());
        var freqs = new List<Freq> { new(BillingInterval.Month, "Every month"), new(BillingInterval.Year, "Every year") };
        _frequency = new ComboBox { ItemsSource = freqs, SelectedIndex = d.Interval == BillingInterval.Year ? 1 : 0 };
        _start = Ui.DateBox(d.StartDate);
        _day = Ui.TextBox((d.DayOfMonth ?? 1).ToString(CultureInfo.InvariantCulture))
            .Also(b => { b.MaxLength = 2; b.Width = 80; b.HorizontalAlignment = HorizontalAlignment.Left; });
        _end = Ui.DateBox(d.EndDate);
        _active = new CheckBox { Content = "Active", IsChecked = d.IsActive, Margin = new Thickness(0, 4, 0, 0) };
        _notes = Ui.TextArea(d.Notes, 56);
        _term1 = Ui.TextBox(d.Term1).Also(t => t.Style = Ui.Style("Input.Small"));
        _term2 = Ui.TextBox(d.Term2).Also(t => t.Style = Ui.Style("Input.Small"));

        // A new schedule's day follows its first date until the user types a day.
        _start.Changed += () =>
        {
            if (_id is null && _start.Date is { } d) _day.Text = d.Day.ToString(CultureInfo.InvariantCulture);
            UpdateNext();
        };
        _day.TextChanged += (_, _) => UpdateNext();
        _frequency.SelectionChanged += (_, _) => UpdateNext();
        _end.Changed += () => UpdateNext();

        var schedule = new StackPanel();
        schedule.Children.Add(F("customer", "Customer", _customer, required: true));
        schedule.Children.Add(Ui.Columns(16,
            (Ui.Star(), F("interval", "How Often", _frequency, required: true)),
            (Ui.Star(), F("start_date", existing is null ? "First Invoice" : "Starting", _start, required: true)
                .Also(f => f.Hint = existing is null ? "A past date creates the missed ones." : null)),
            (Ui.Star(), F("day_of_month", "On Day", _day, required: true).Also(f => f.Hint = "Shorter months use their last day.")),
            (Ui.Star(), F("end_date", "Ending", _end).Also(f => f.Hint = "Optional."))));
        schedule.Children.Add(_active);

        _lines = new LineBuilder("No line items yet. Click Add Line to get started.", LineDates.Period);
        _lines.Changed += () => _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);
        if (existing is not null)
            _lines.SetRows(existing.Lines.Select(l => new InvoiceRowInput(l.Description, InvoiceDetailPage.QtyText(l.Quantity), PyMath.JsToFixedText(l.UnitPrice, 2), BillPeriod: l.BillPeriod)));
        var headerTools = Ui.Row(8);
        var items = Store.Items.Active();
        if (items.Count > 0)
        {
            var picker = new ComboBox { Width = 240, ItemsSource = items.Select(i => $"{i.Description} — {Ui.Money(i.Price)}").ToList(), MinHeight = 31, FontSize = 13.6 };
            Input.SetPlaceholder(picker, "Quick-add service…");
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var item = items[picker.SelectedIndex];
                _lines.AddRow(item.Description, "1", PyMath.JsToFixedText(item.Price, 2));
                picker.SelectedIndex = -1;
            };
            headerTools.Children.Add(picker);
        }
        headerTools.Children.Add(Ui.Button("Add Line", "Btn.OutlinePrimary", "plus-lg", () => _lines.AddRow("", "1", "", focus: true), small: true).Margin(8, 0, 0, 0));
        var linesBody = Ui.Stack(0, _lines,
            new Border { Padding = new Thickness(16, 10, 16, 12), Child = Ui.Muted("Tip: type {month} and {year} to show the billing period, e.g. \"Lawn care - {month} {year}\" prints as \"Lawn care - November 2026\" (works in Notes too). The calendar button on a line prints the period as service dates.", 12.8)
                .Also(t => t.TextWrapping = TextWrapping.Wrap) });

        var terms = Ui.Stack(0,
            Ui.Field("Notes", _notes),
            Ui.Columns(16, (Ui.Star(), Ui.Field("Payment Terms", _term1).Margin(0, 0, 0, 0)), (Ui.Star(), Ui.Field("Additional Terms", _term2).Margin(0, 0, 0, 0))));

        var left = Ui.Stack(24,
            Ui.Card(schedule, "Schedule", "arrow-repeat"),
            Ui.Card(linesBody, "Line Items", "list-ul", headerRight: headerTools, bodyPadding: new Thickness(0)),
            Ui.Card(terms));

        var summary = new StackPanel();
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_summaryTotal, Dock.Right);
        _summaryTotal.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        totalRow.Children.Add(_summaryTotal);
        totalRow.Children.Add(Ui.Muted("Each Invoice", 15).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        summary.Children.Add(totalRow);
        summary.Children.Add(_nextText.Margin(0, 0, 0, 16));
        summary.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 0, 0, 16) });
        summary.Children.Add(Ui.Button("Save Recurring Invoice", "Btn.Primary", "check-lg", Save).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Padding = new Thickness(16, 9, 16, 9); b.FontSize = 17.6; }));
        summary.Children.Add(Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Back()))
            .Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(0, 8, 0, 0));
        _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(existing is null ? "New Recurring Invoice" : $"Edit Recurring Invoice",
            existing?.Customer?.Name));
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), left), (Ui.Star(1), Ui.Card(summary, "Summary"))));
        Content = page;
        UpdateNext(existing);

        _day.KeyDown += (_, k) => { if (k.Key == Key.Enter) Save(); };
    }

    public override void OnShown()
    {
        if (_customer.SelectedId is null) _customer.FocusBox();
        else if (_lines.Count == 0) _lines.AddRow("", "1", "", focus: true, byUser: false);
    }

    private Func<AppPage> Back() =>
        _fromCustomer is { } cid ? Routes.Customer(Shell, cid) : Routes.RecurringInvoices(Shell);

    /// <summary>"Next invoice: Nov 01, 2026" under the total, from what is typed so far.</summary>
    private void UpdateNext(RecurringInvoice? existing = null)
    {
        if (_start.Date is not { } start || !int.TryParse(_day.Text.Trim(), out int day) || day is < 1 or > 31)
        {
            _nextText.Text = "";
            return;
        }
        var t = new RecurringInvoice
        {
            StartDate = start, DayOfMonth = day, EndDate = _end.Date,
            Interval = (_frequency.SelectedItem as Freq)?.Value ?? BillingInterval.Month,
        };
        if (_id is { } id) t.GeneratedThrough = (existing ?? SafeGet(id))?.GeneratedThrough;
        _nextText.Text = t.NextDate is { } n ? $"Next invoice: {Ui.LongDate(n)}, then {t.ScheduleLabel.ToLowerInvariant()}." : "No more invoices: the end date has passed.";
    }

    private RecurringInvoice? SafeGet(long id)
    {
        try { return Store.RecurringInvoices.Get(id); }
        catch (UserFacingException) { return null; }
    }

    private Field F(string key, string label, UIElement input, bool required = false) => _fields[key] = Ui.Field(label, input, required);

    private async void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox tb) Input.SetInvalid(tb, false); }
        _customer.Invalid = false;
        var bad = new Dictionary<string, string>();
        if (_customer.HasUnmatchedText) bad["customer"] = QuickAdd.CustomerProblem;
        int? day = null;
        if (_day.Text.Trim().Length > 0)
        {
            if (int.TryParse(_day.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int d)) day = d;
            else bad["day_of_month"] = "Enter a day from 1 to 31.";
        }
        if (!_start.IsBlank && _start.Date is null) bad["start_date"] = "Enter a date like 2026-11-01.";
        if (!_end.IsBlank && _end.Date is null) bad["end_date"] = "Enter a date like 2027-11-01.";
        if (bad.Count > 0) { ShowFieldErrors(bad); Shell.ShowError(string.Join(" ", bad.Values.Distinct())); return; }

        var draft = new RecurringInvoiceDraft(_customer.SelectedId, ((Freq)_frequency.SelectedItem).Value,
            _start.Date, day, _end.Date, _notes.Text, _term1.Text, _term2.Text, _active.IsChecked == true,
            _lines.Rows.Select(InvoiceRows.ToDraft).ToList());

        int past = 0;
        if (!Try(() => past = Store.RecurringInvoices.PreviewCount(draft, _id))) return;
        if (past > 0)
        {
            string due = past == 1 && draft.StartDate == DateOnly.FromDateTime(DateTime.Today)
                ? "The first invoice is due today, so saving this creates it now."
                : $"The first date is in the past, so saving this creates {past} {(past == 1 ? "invoice" : "invoices")} now, one for each date through today, each with its own date and number.";
            bool go = await Shell.Confirm($"{due} Continue?",
                $"Create {past} {(past == 1 ? "Invoice" : "Invoices")}", title: "Create invoices now?");
            if (!go) return;
        }
        Try(() =>
        {
            Notice notice = _id is { } id ? Store.RecurringInvoices.Update(id, draft) : Store.RecurringInvoices.Create(draft).Notice;
            var run = Store.RecurringInvoices.GenerateDue();
            if (RecurringInvoiceService.Summarize(run) is { } s) notice = new Notice(notice.Message + " " + s.Message, s.Kind == NoticeKind.Warning ? s.Kind : notice.Kind);
            Shell.Navigate(_fromCustomer is { } cid ? Routes.Customer(Shell, cid) : Routes.RecurringInvoices(Shell), notice);
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, message) in errors)
            if (_fields.TryGetValue(key, out var f))
            {
                f.Error = message;
                if (f.Content is TextBox t) Input.SetInvalid(t, true);
                if (f.Content is ComboBox c) Input.SetInvalid(c, true);
                if (f.Content is RecordPicker rp) rp.Invalid = true;
            }
    }
}
