using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.4 recurring invoices: schedules, generation, early issue, skip, cascades.</summary>
public class RecurringInvoiceTests
{
    /// <summary>A store whose clock the test moves.</summary>
    private sealed class Clock
    {
        public DateOnly Day;
        public Store Store = null!;
        public RecurringInvoiceService R => Store.RecurringInvoices;
    }

    private static Clock Open(DateOnly day)
    {
        var c = new Clock { Day = day };
        c.Store = new Store(Fixture.TempPaths(), () => c.Day);
        return c;
    }

    private static long Customer(Store s, string name = "Maple Street HOA", bool active = true) =>
        s.Customers.Create(new CustomerDraft(name, "", "", "", "", "", "", "", "", active)).Id;

    private static RecurringInvoiceDraft Draft(long customer, DateOnly start, string interval = BillingInterval.Month,
        int? day = null, DateOnly? end = null, bool active = true, string notes = "", params InvoiceLineDraft[] lines) =>
        new(customer, interval, start, day ?? start.Day, end, notes, "Due in 30 days", "Thanks", active,
            lines.Length > 0 ? lines : new[] { new InvoiceLineDraft("Lawn care - {month} {year}", 1, 150) });

    private static List<Invoice> Invoices(Store s) => s.Invoices.List().OrderBy(i => i.InvoiceNumber).ToList();

    // ------------------------------------------------------------------ schema and rules

    [Fact]
    public void Tables_exist_and_a_1x_database_opens_with_them()
    {
        var store = new Store(Fixture.CopyOf("parity.db"), () => new DateOnly(2026, 10, 2));
        store.Database.Run(db =>
        {
            foreach (string t in new[] { "recurring_invoices", "recurring_invoice_lines", "recurring_invoice_runs" })
                Assert.True(db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t)", new { t }), t);
        });
        Assert.Empty(store.RecurringInvoices.List());
        Assert.Equal((0, 0.0), store.RecurringInvoices.DueWithin());
    }

    [Fact]
    public void Period_text_fills_month_and_year_in_any_case_and_leaves_other_braces()
    {
        var d = new DateOnly(2026, 11, 1);
        Assert.Equal("Lawn care - November 2026", PeriodText.Fill("Lawn care - {month} {year}", d));
        Assert.Equal("NOVEMBER/2026 {other}", PeriodText.Fill("{MONTH}/{Year} {other}", d).Replace("November", "NOVEMBER"));
        Assert.Equal("", PeriodText.Fill(null, d));
    }

    [Fact]
    public void Schedule_clamps_day_31_and_yearly_feb_29_like_recurring_expenses()
    {
        var monthly = new Schedule(new DateOnly(2027, 1, 31), 1, 31, null);
        Assert.Equal(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31) },
            monthly.Occurrences(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31)));
        var yearly = new Schedule(new DateOnly(2028, 2, 29), 12, 29, null);
        Assert.Equal(new[] { new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28) },
            yearly.Occurrences(new DateOnly(2028, 1, 1), new DateOnly(2029, 12, 31)));
    }

    [Fact]
    public void Validation_names_the_fields_and_requires_a_line()
    {
        var c = Open(new DateOnly(2026, 10, 2));
        var bad = Assert.Throws<ValidationException>(() => c.R.Create(new RecurringInvoiceDraft(null, "weekly", null, 40, null, "", "", "", true,
            new[] { new InvoiceLineDraft("x", 1, 1) })));
        Assert.Equal(new[] { "customer", "day_of_month", "interval", "start_date" }, bad.Fields.Keys.OrderBy(k => k));
        long cust = Customer(c.Store);
        Assert.Throws<UserFacingException>(() => c.R.Create(Draft(cust, c.Day, lines: new InvoiceLineDraft("  ", 1, 0))));
        var ended = Assert.Throws<ValidationException>(() => c.R.Create(Draft(cust, c.Day, end: c.Day.AddDays(-1))));
        Assert.Contains("end_date", ended.Fields.Keys);
    }

    // ------------------------------------------------------------------ generation

    [Fact]
    public void Creates_the_invoice_on_its_date_once_with_the_next_number_and_period_text()
    {
        var c = Open(new DateOnly(2026, 10, 2));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1), notes: "Service for {month}")).Id;
        Assert.Equal(0, c.R.PreviewCount(Draft(cust, new DateOnly(2026, 11, 1))));
        Assert.Empty(c.R.GenerateDue().Created);                      // not due yet

        c.Day = new DateOnly(2026, 11, 1);
        var run = c.R.GenerateDue();
        var made = Assert.Single(run.Created);
        Assert.Empty(c.R.GenerateDue().Created);                      // running again creates nothing

        var inv = c.Store.Invoices.Get(made.InvoiceId);
        Assert.Equal("INV1001", inv.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 11, 1), inv.Date);
        Assert.Equal("Lawn care - November 2026", Assert.Single(inv.Lines).Description);
        Assert.Equal(150, inv.Total);
        Assert.Equal("Service for November", inv.Notes);
        Assert.Equal("Due in 30 days", inv.Term1);
        Assert.False(inv.Paid);
        Assert.Equal(new DateOnly(2026, 12, 1), c.R.Get(id).NextDate);
        Assert.Equal("1 recurring invoice created: INV1001 Maple Street HOA (Nov 01).", RecurringInvoiceService.Summarize(run)!.Message);
    }

    [Fact]
    public void Missed_dates_are_each_created_with_their_own_date_and_consecutive_numbers()
    {
        var c = Open(new DateOnly(2026, 10, 2));
        long cust = Customer(c.Store);
        var draft = Draft(cust, new DateOnly(2026, 8, 15));
        Assert.Equal(2, c.R.PreviewCount(draft));                     // Aug 15, Sep 15
        c.R.Create(draft);
        c.Day = new DateOnly(2026, 12, 20);                           // app not opened for a while
        var run = c.R.GenerateDue();
        Assert.Equal(new[] { "2026-08-15", "2026-09-15", "2026-10-15", "2026-11-15", "2026-12-15" },
            run.Created.Select(i => i.Date.ToString("yyyy-MM-dd")));
        Assert.Equal(new[] { "INV1001", "INV1002", "INV1003", "INV1004", "INV1005" }, Invoices(c.Store).Select(i => i.InvoiceNumber));
        Assert.Equal("Lawn care - September 2026", Invoices(c.Store)[1].Lines[0].Description);
        Assert.Equal(5, c.Store.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM recurring_invoice_runs")));
    }

    [Fact]
    public void Yearly_schedules_and_the_end_date_stop_where_they_should()
    {
        var c = Open(new DateOnly(2026, 1, 1));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 3, 5), BillingInterval.Year, end: new DateOnly(2028, 3, 4))).Id;
        Assert.Equal("Yearly on Mar 5", c.R.Get(id).ScheduleLabel);
        c.Day = new DateOnly(2030, 1, 1);
        Assert.Equal(new[] { 2026, 2027 }, c.R.GenerateDue().Created.Select(i => i.Date.Year));
        Assert.Null(c.R.Get(id).NextDate);
        Assert.Empty(c.R.Upcoming());
    }

    [Fact]
    public void Issuing_early_keeps_the_scheduled_date_and_the_date_itself_creates_nothing_more()
    {
        var c = Open(new DateOnly(2026, 10, 20));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1))).Id;
        var preview = c.R.Preview(id, new DateOnly(2026, 11, 1));
        Assert.Equal("INV1001", preview.InvoiceNumber);
        Assert.Equal("Maple Street HOA", preview.Customer!.Name);
        Assert.Equal(0, c.Store.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM invoices")));   // preview saves nothing

        var issued = c.R.Issue(id, new DateOnly(2026, 11, 1));
        var inv = c.Store.Invoices.Get(issued.InvoiceId);
        Assert.Equal(preview.InvoiceNumber, inv.InvoiceNumber);
        Assert.Equal(preview.Date, inv.Date);
        Assert.Equal(preview.Lines.Select(l => (l.Description, l.Quantity, l.Amount)), inv.Lines.Select(l => (l.Description, l.Quantity, l.Amount)));
        Assert.Throws<UserFacingException>(() => c.R.Issue(id, new DateOnly(2026, 11, 1)));      // no second copy
        Assert.Equal(new DateOnly(2026, 12, 1), c.R.Get(id).NextDate);

        c.Day = new DateOnly(2026, 11, 2);
        Assert.Empty(c.R.GenerateDue().Created);
        Assert.Single(c.Store.Invoices.List());
    }

    [Fact]
    public void A_deleted_invoice_is_not_created_again_and_skip_passes_one_date()
    {
        var c = Open(new DateOnly(2026, 11, 1));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1))).Id;
        var made = Assert.Single(c.R.GenerateDue().Created);
        c.Store.Invoices.Delete(made.InvoiceId);
        Assert.Empty(c.R.GenerateDue().Created);
        Assert.Null(c.Store.Database.Run(db => db.ExecuteScalar<long?>("SELECT invoice_id FROM recurring_invoice_runs")));

        Assert.Throws<UserFacingException>(() => c.R.Skip(id, new DateOnly(2027, 1, 1)));        // only the next date
        Assert.Contains("Jan 1, 2027", c.R.Skip(id, new DateOnly(2026, 12, 1)).Message);
        c.Day = new DateOnly(2027, 1, 5);
        Assert.Equal(new DateOnly(2027, 1, 1), Assert.Single(c.R.GenerateDue().Created).Date);
    }

    [Fact]
    public void Pausing_then_resuming_skips_the_paused_dates()
    {
        var c = Open(new DateOnly(2026, 11, 1));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1))).Id;
        Assert.Single(c.R.GenerateDue().Created);
        c.R.ToggleActive(id);
        c.Day = new DateOnly(2027, 2, 10);
        Assert.Empty(c.R.GenerateDue().Created);
        Assert.Empty(c.R.Upcoming());
        c.R.ToggleActive(id);
        Assert.Empty(c.R.GenerateDue().Created);                      // Dec, Jan and Feb 1 are skipped
        Assert.Equal(new DateOnly(2027, 3, 1), c.R.Get(id).NextDate);
    }

    [Fact]
    public void An_inactive_customer_holds_its_schedule_back_and_says_so()
    {
        var c = Open(new DateOnly(2026, 11, 1));
        long cust = Customer(c.Store);
        long other = Customer(c.Store, "Hillcrest");
        c.R.Create(Draft(cust, new DateOnly(2026, 11, 1)));
        c.R.Create(Draft(other, new DateOnly(2026, 11, 1)));
        c.Store.Customers.Update(cust, new CustomerDraft("Maple Street HOA", "", "", "", "", "", "", "", "", false));
        var run = c.R.GenerateDue();
        Assert.Equal("Hillcrest", Assert.Single(run.Created).CustomerName);
        Assert.Contains("Maple Street HOA is inactive", Assert.Single(run.Problems));
        Assert.Equal(NoticeKind.Warning, RecurringInvoiceService.Summarize(run)!.Kind);
    }

    [Fact]
    public void Deleting_a_schedule_keeps_its_invoices_and_deleting_the_customer_removes_the_schedule()
    {
        var c = Open(new DateOnly(2026, 11, 1));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1))).Id;
        c.R.GenerateDue();
        Assert.Contains("is kept", c.R.Delete(id).Message);
        Assert.Single(c.Store.Invoices.List());

        c.R.Create(Draft(cust, new DateOnly(2026, 12, 1)));
        c.Store.Customers.Delete(cust);
        c.Store.Database.Run(db =>
        {
            foreach (string t in new[] { "recurring_invoices", "recurring_invoice_lines", "recurring_invoice_runs", "invoices" })
                Assert.Equal(0, db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {t}"));
        });
    }

    [Fact]
    public void Upcoming_and_the_dashboard_count_look_30_days_ahead()
    {
        var c = Open(new DateOnly(2026, 10, 2));
        long a = Customer(c.Store, "A");
        long b = Customer(c.Store, "B");
        c.R.Create(Draft(a, new DateOnly(2026, 10, 20)));                                   // Oct 20, then Nov 20
        c.R.Create(Draft(b, new DateOnly(2026, 12, 15), lines: new InvoiceLineDraft("Retainer", 2, 250.5)));
        Assert.Equal(new[] { "A", "B" }, c.R.Upcoming().Select(u => u.Schedule.Customer!.Name));
        Assert.Equal(new DateOnly(2026, 10, 20), c.R.Upcoming()[0].Date);
        Assert.Single(c.R.Upcoming(c.Day.AddDays(30)));
        Assert.Equal((1, 150.0), c.R.DueWithin(30));
        c.Day = new DateOnly(2026, 10, 25);                           // Nov 20 is inside 30 days
        Assert.Equal((2, 150.0 + 150.0), c.R.DueWithin(30));          // Oct 20 not created yet (app not restarted) still counts
        Assert.Equal(250.5, c.R.Upcoming()[1].Total);              // a line amount is its extended total
    }

    [Fact]
    public void Editing_changes_future_invoices_only()
    {
        var c = Open(new DateOnly(2026, 11, 1));
        long cust = Customer(c.Store);
        long id = c.R.Create(Draft(cust, new DateOnly(2026, 11, 1))).Id;
        c.R.GenerateDue();
        c.R.Update(id, Draft(cust, new DateOnly(2026, 11, 1), lines: new InvoiceLineDraft("New rate", 1, 175)));
        Assert.Equal(150, Assert.Single(c.Store.Invoices.List()).Total);
        Assert.Equal(0, c.R.PreviewCount(Draft(cust, new DateOnly(2026, 11, 1)), id));
        c.Day = new DateOnly(2026, 12, 1);
        var dec = Assert.Single(c.R.GenerateDue().Created);
        Assert.Equal(175, dec.Total);
    }
}
