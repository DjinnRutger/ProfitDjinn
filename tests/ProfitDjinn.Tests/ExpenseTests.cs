using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.2 Expenses: vendors, categories, expenses and payments, receipts, recurring.</summary>
public class ExpenseTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    // ------------------------------------------------------------------ schema and seed

    [Fact]
    public void Expense_tables_exist_categories_are_seeded_once_and_the_feature_starts_off()
    {
        var paths = Fixture.TempPaths();
        var store = new Store(paths, () => Today);
        store.Database.Run(db =>
        {
            foreach (string t in new[] { "vendors", "expense_categories", "expenses", "expense_payments", "expense_receipts", "recurring_expenses" })
                Assert.True(db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t)", new { t }), t);
            Assert.Equal(Seed.StarterCategories, db.Query<string>("SELECT name FROM expense_categories ORDER BY sort_order"));
            db.Execute("DELETE FROM expense_categories");
        });
        Assert.Equal("false", store.Settings.Get(SettingKeys.ExpensesEnabled));
        Assert.Equal("", store.Settings.Get(SettingKeys.ReceiptsFolder));

        var reopened = new Store(paths, () => Today);           // deleted starters stay deleted
        Assert.Equal(0, reopened.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM expense_categories")));
    }

    [Fact]
    public void A_1x_database_gets_the_expense_tables_and_starter_categories_on_upgrade()
    {
        var store = new Store(Fixture.CopyOf("parity.db"), () => Today);
        Assert.Equal(Seed.StarterCategories.Length, store.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM expense_categories")));
    }

    // ------------------------------------------------------------------ vendors and categories

    private static VendorDraft Vendor(string name, string email = "", long? category = null, bool active = true) =>
        new(name, "", "", "", "mt", "", "", email, category, "", active);

    [Fact]
    public void Vendors_validate_like_customers_and_list_by_name()
    {
        var store = Fixture.FreshStore(Today);
        var v = store.Vendors;
        var bad = Assert.Throws<ValidationException>(() => v.Create(Vendor("  ", email: "nope")));
        Assert.Contains("name", bad.Fields.Keys);
        Assert.Contains("email", bad.Fields.Keys);
        Assert.Throws<ValidationException>(() => v.Create(Vendor("X", category: 9999)));

        long zed = v.Create(Vendor("Zed Hardware", email: "Sales@Zed.com")).Id;
        v.Create(Vendor("acme supply"));
        v.Create(Vendor("Old Co", active: false));
        Assert.Equal(new[] { "acme supply", "Zed Hardware" }, v.List().Select(x => x.Name));
        Assert.Equal(3, v.List(includeInactive: true).Count);
        Assert.Equal(new[] { "Zed Hardware" }, v.List("zed").Select(x => x.Name));
        var got = v.Get(zed);
        Assert.Equal("sales@zed.com", got.Email);
        Assert.Equal("MT", got.State);
        Assert.Equal(2, v.ActiveForPicker().Count);
    }

    [Fact]
    public void A_vendor_with_expenses_cannot_be_deleted_but_an_unused_one_can()
    {
        var store = Fixture.FreshStore(Today);
        long used = store.Vendors.Create(Vendor("Used")).Id;
        long unused = store.Vendors.Create(Vendor("Unused")).Id;
        long cat = store.Categories.Active()[0].Id;
        store.Database.Run(db => db.Execute(
            "INSERT INTO expenses (vendor_id, category_id, date, description, amount) VALUES (@used, @cat, '2026-10-01', 'x', 5)", new { used, cat }));
        var ex = Assert.Throws<UserFacingException>(() => store.Vendors.Delete(used));
        Assert.Contains("1 expense", ex.Message);
        store.Vendors.Delete(unused);
        Assert.Single(store.Vendors.List(includeInactive: true));
    }

    [Fact]
    public void Categories_add_before_Other_rename_hide_and_refuse_duplicates_or_deleting_one_in_use()
    {
        var store = Fixture.FreshStore(Today);
        var c = store.Categories;
        long fuel = c.Create("  Fuel ").Id;
        var names = c.Active().Select(x => x.Name).ToList();
        Assert.Equal("Fuel", names[^2]);
        Assert.Equal("Other", names[^1]);

        Assert.Contains("name", Assert.Throws<ValidationException>(() => c.Create("fuel")).Fields.Keys);
        Assert.Throws<ValidationException>(() => c.Create(""));
        Assert.Throws<ValidationException>(() => c.Rename(fuel, "SUPPLIES"));
        c.Rename(fuel, "Fuel & Oil");
        c.Rename(fuel, "fuel & oil");                           // same category, new case: allowed

        c.ToggleActive(fuel);
        Assert.DoesNotContain(c.Active(), x => x.Id == fuel);
        Assert.Contains(c.List(), u => u.Category.Id == fuel && !u.Category.IsActive);
        Assert.DoesNotContain(c.List(includeHidden: false), u => u.Category.Id == fuel);

        long supplies = c.Active().Single(x => x.Name == "Supplies").Id;
        store.Database.Run(db => db.Execute(
            "INSERT INTO expenses (category_id, date, description, amount) VALUES (@supplies, '2026-10-01', 'x', 5)", new { supplies }));
        Assert.Throws<UserFacingException>(() => c.Delete(supplies));
        Assert.Equal(1, c.List().Single(u => u.Category.Id == supplies).Expenses);
        c.Delete(fuel);
        Assert.DoesNotContain(c.List(), u => u.Category.Id == fuel);
    }

    // ------------------------------------------------------------------ expenses and payments

    private static long Cat(Store s, string name = "Supplies") => s.Categories.Active().Single(c => c.Name == name).Id;

    private static ExpenseDraft Draft(Store s, double? amount = 100, long? vendor = null, DateOnly? date = null,
        DateOnly? due = null, string desc = "Printer paper", PaidNow? paid = null) =>
        new(vendor, Cat(s), date ?? Today, due, desc, "INV-77", amount, "", paid);

    [Fact]
    public void An_expense_validates_its_fields()
    {
        var s = Fixture.FreshStore(Today);
        var e = Assert.Throws<ValidationException>(() => s.Expenses.Create(new ExpenseDraft(null, null, null, null, "", "", null, "")));
        Assert.Equal(new[] { "amount", "category_id", "date", "description" }, e.Fields.Keys.OrderBy(k => k));
        Assert.Contains("amount", Assert.Throws<ValidationException>(() => s.Expenses.Create(Draft(s, amount: 0))).Fields.Keys);
        Assert.Contains("amount", Assert.Throws<ValidationException>(() => s.Expenses.Create(Draft(s, amount: 1.005))).Fields.Keys);
        Assert.Contains("due_date", Assert.Throws<ValidationException>(() => s.Expenses.Create(Draft(s, due: Today.AddDays(-1)))).Fields.Keys);
        Assert.Contains("vendor_id", Assert.Throws<ValidationException>(() => s.Expenses.Create(Draft(s, vendor: 42))).Fields.Keys);
        long gone = s.Vendors.Create(Vendor("Gone", active: false)).Id;
        Assert.Contains("vendor_id", Assert.Throws<ValidationException>(() => s.Expenses.Create(Draft(s, vendor: gone))).Fields.Keys);
    }

    [Fact]
    public void Payments_move_an_expense_from_unpaid_to_partial_to_paid_and_cannot_overpay()
    {
        var s = Fixture.FreshStore(Today);
        long v = s.Vendors.Create(Vendor("Acme")).Id;
        long id = s.Expenses.Create(Draft(s, vendor: v, due: Today.AddDays(10))).Id;
        Assert.Equal(InvoiceStatus.Unpaid, s.Expenses.Get(id).Status);

        var n = s.Expenses.RecordPayment(id, 40, "check", " 1001 ", Today, "first");
        Assert.Equal("Partial payment of $40.00 recorded. Balance remaining: $60.00.", n.Message);
        var e = s.Expenses.Get(id);
        Assert.Equal(InvoiceStatus.Partial, e.Status);
        Assert.Equal("1001", e.Payments.Single().CheckNumber);

        Assert.Contains("$60.00 balance", Assert.Throws<UserFacingException>(() => s.Expenses.RecordPayment(id, 60.01, "cash", null, null, null)).Message);
        Assert.Contains("amount", Assert.Throws<ValidationException>(() => s.Expenses.Update(id, Draft(s, amount: 39.99, vendor: v))).Fields.Keys);

        Assert.Equal("Payment of $60.00 recorded. Expense paid in full.", s.Expenses.RecordPayment(id, 60, "ach", null, Today, null).Message);
        e = s.Expenses.Get(id);
        Assert.Equal(InvoiceStatus.Paid, e.Status);
        Assert.Equal(0, e.BalanceDue);
        Assert.Equal("Check, ACH", e.PaidVia);
        Assert.Throws<UserFacingException>(() => s.Expenses.RecordPayment(id, 1, "cash", null, null, null));

        s.Expenses.DeletePayment(id, e.Payments[1].Id);
        Assert.Equal(InvoiceStatus.Partial, s.Expenses.Get(id).Status);
        Assert.Equal(100, s.Vendors.Get(v).TotalBilled);
        Assert.Equal(40, s.Vendors.Get(v).TotalPaid);
        Assert.Equal(60, s.Vendors.Get(v).Owed);
    }

    [Fact]
    public void Already_paid_records_one_full_payment_and_delete_removes_everything()
    {
        var s = Fixture.FreshStore(Today);
        long id = s.Expenses.Create(Draft(s, amount: 19.99, paid: new PaidNow("credit_card", null, null))).Id;
        var e = s.Expenses.Get(id);
        Assert.Equal(InvoiceStatus.Paid, e.Status);
        Assert.Equal(Today, e.Payments.Single().Date);
        Assert.Equal(19.99, e.Payments.Single().Amount);

        s.Expenses.Delete(id);
        Assert.Throws<UserFacingException>(() => s.Expenses.Get(id));
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM expense_payments")));
    }

    [Fact]
    public void The_list_filters_and_searches_and_the_summary_counts_this_year_unpaid_and_overdue()
    {
        var s = Fixture.FreshStore(Today);
        long acme = s.Vendors.Create(Vendor("Acme")).Id;
        s.Expenses.Create(Draft(s, amount: 50, vendor: acme, desc: "Toner", date: Today.AddDays(-20), due: Today.AddDays(-5)));  // overdue
        s.Expenses.Create(Draft(s, amount: 25, desc: "Stamps", paid: new PaidNow("cash", null, null)));
        s.Expenses.Create(new ExpenseDraft(null, Cat(s, "Software & Subscriptions"), new DateOnly(2025, 12, 31), null, "Old licence", "", 10, ""));

        Assert.Equal(new[] { "Stamps", "Toner", "Old licence" }, s.Expenses.List().Select(e => e.Description));
        Assert.Equal(new[] { "Toner", "Old licence" }, s.Expenses.List(ExpenseFilter.Unpaid).Select(e => e.Description));
        Assert.Equal(new[] { "Stamps" }, s.Expenses.List(ExpenseFilter.Paid).Select(e => e.Description));
        Assert.Equal(new[] { "Toner" }, s.Expenses.List(search: "acme").Select(e => e.Description));
        Assert.Equal(new[] { "Old licence" }, s.Expenses.List(categoryId: Cat(s, "Software & Subscriptions")).Select(e => e.Description));

        var sum = s.Expenses.Summary();
        Assert.Equal(75, sum.YearTotal);
        Assert.Equal(2, sum.UnpaidCount);
        Assert.Equal(60, sum.UnpaidTotal);
        Assert.Equal(1, sum.OverdueCount);
        Assert.Equal(50, sum.OverdueTotal);
        Assert.False(s.Expenses.Enabled);
    }

    // ------------------------------------------------------------------ receipts

    private static string TempFile(string name, int bytes = 64)
    {
        string dir = Path.Combine(Path.GetTempPath(), "profitdjinn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)7, bytes).ToArray());
        return path;
    }

    [Fact]
    public void Receipts_are_copied_into_the_data_folder_by_year_and_open_from_there()
    {
        var s = Fixture.FreshStore(Today);
        long id = s.Expenses.Create(Draft(s)).Id;
        string pdf = TempFile("Office Depot #12.pdf");
        s.Expenses.AddReceipts(id, new[] { pdf, TempFile("Office Depot #12.pdf") });

        var r = s.Expenses.Get(id).Receipts;
        Assert.Equal(2, r.Count);
        Assert.Equal(Path.Combine("2026", "E1-Office Depot #12.pdf"), r[0].RelPath);
        Assert.Equal(Path.Combine("2026", "E1-Office Depot #12-2.pdf"), r[1].RelPath);
        Assert.Equal(s.Paths.ReceiptsFolder, r[0].Folder);
        Assert.Equal(64, r[0].SizeBytes);
        string opened = s.Expenses.ReceiptPath(r[0].Id);
        Assert.StartsWith(s.Paths.ReceiptsFolder, opened);
        Assert.True(File.Exists(pdf));                          // the original is left where it was

        Assert.Contains("Nothing was attached", Assert.Throws<UserFacingException>(() =>
            s.Expenses.AddReceipts(id, new[] { TempFile("ok.png"), TempFile("notes.docx") })).Message);
        Assert.Throws<UserFacingException>(() => s.Expenses.AddReceipts(id, new[] { TempFile("empty.jpg", 0) }));
        Assert.Equal(2, s.Expenses.ReceiptCount());

        s.Expenses.RemoveReceipt(r[1].Id);
        Assert.False(File.Exists(Path.Combine(s.Paths.ReceiptsFolder, r[1].RelPath)));
        Assert.Equal("Expense 'Printer paper' and 1 receipt deleted.", s.Expenses.Delete(id).Message);
        Assert.False(File.Exists(opened));
    }

    [Fact]
    public void Changing_the_folder_without_moving_keeps_old_receipts_openable_and_moving_updates_files_and_rows()
    {
        var s = Fixture.FreshStore(Today);
        long id = s.Expenses.Create(Draft(s)).Id;
        s.Expenses.AddReceipts(id, new[] { TempFile("a.jpg") });
        var oldR = s.Expenses.Get(id).Receipts.Single();

        string cloud = Path.Combine(Path.GetTempPath(), "profitdjinn-tests", Guid.NewGuid().ToString("N"), "Receipts");
        Assert.Null(ReceiptStore.ValidateFolder(cloud));
        s.Settings.Set(SettingKeys.ReceiptsFolder, cloud);      // "Leave them"
        Assert.Equal(cloud, s.Receipts.CurrentFolder);
        Assert.StartsWith(s.Paths.ReceiptsFolder, s.Expenses.ReceiptPath(oldR.Id));

        s.Expenses.AddReceipts(id, new[] { TempFile("b.png") });
        Assert.Equal(cloud, s.Expenses.Get(id).Receipts[1].Folder);

        var result = s.Expenses.MoveReceipts(cloud);            // "Move them"
        Assert.Equal(2, result.Moved);
        Assert.Equal(0, result.Missing);
        Assert.Empty(result.Failed);
        Assert.All(s.Expenses.Get(id).Receipts, r => Assert.Equal(cloud, r.Folder));
        Assert.StartsWith(cloud, s.Expenses.ReceiptPath(oldR.Id));
        Assert.False(File.Exists(Path.Combine(s.Paths.ReceiptsFolder, oldR.RelPath)));

        File.Delete(s.Expenses.ReceiptPath(oldR.Id));           // gone outside the app
        Assert.Contains("was not found", Assert.Throws<UserFacingException>(() => s.Expenses.ReceiptPath(oldR.Id)).Message);
        Assert.Equal(1, s.Expenses.MoveReceipts(s.Paths.ReceiptsFolder).Missing);
    }

    // ------------------------------------------------------------------ recurring

    private static RecurringExpense Template(string freq, DateOnly start, int day, DateOnly? end = null) =>
        new() { Frequency = freq, StartDate = start, DayOfMonth = day, EndDate = end };

    [Fact]
    public void Day_31_lands_on_the_last_day_of_short_months_and_yearly_Feb_29_on_Feb_28()
    {
        var monthly = Template(RecurringFrequency.Monthly, new DateOnly(2027, 1, 31), 31);
        Assert.Equal(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30) },
            RecurringService.Occurrences(monthly, monthly.StartDate, new DateOnly(2027, 4, 30)));
        Assert.Contains(new DateOnly(2028, 2, 29), RecurringService.Occurrences(monthly, new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)));

        var yearly = Template(RecurringFrequency.Yearly, new DateOnly(2028, 2, 29), 29);
        Assert.Equal(new[] { new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28), new DateOnly(2032, 2, 29) },
            RecurringService.Occurrences(yearly, yearly.StartDate, new DateOnly(2032, 12, 31)).Where(d => d.Year is 2028 or 2029 or 2032));

        // Starting on the 20th with day 5: the first one is next month's 5th. The end date stops it.
        var late = Template(RecurringFrequency.Monthly, new DateOnly(2026, 1, 20), 5, end: new DateOnly(2026, 4, 5));
        Assert.Equal(new[] { new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 5), new DateOnly(2026, 4, 5) },
            RecurringService.Occurrences(late, late.StartDate, new DateOnly(2027, 1, 1)));
    }

    private static RecurringDraft Rent(Store s, DateOnly start, string mode = RecurringMode.Paid, bool active = true, int day = 1) =>
        new(null, Cat(s, "Rent/Lease"), "Office rent", 1200, RecurringFrequency.Monthly, start, day, null, mode,
            mode == RecurringMode.Paid ? "ach" : null, "", active);

    [Fact]
    public void Generation_creates_each_due_expense_once_and_never_recreates_a_deleted_one()
    {
        var day = new DateOnly(2026, 10, 2);
        var s = new Store(Fixture.TempPaths(), () => day);
        long id = s.Recurring.Create(Rent(s, new DateOnly(2026, 8, 1))).Id;
        Assert.Equal(3, s.Recurring.PreviewCount(Rent(s, new DateOnly(2026, 8, 1))));
        Assert.Empty(s.Recurring.GenerateDue());                 // Expenses is off

        s.Settings.Set(SettingKeys.ExpensesEnabled, "true");
        var made = s.Recurring.GenerateDue();
        Assert.Equal(new[] { new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1) }, made.Select(g => g.Date));
        Assert.All(s.Expenses.List(), e => { Assert.Equal(InvoiceStatus.Paid, e.Status); Assert.Equal("ACH", e.PaidVia); Assert.Null(e.DueDate); });
        Assert.Equal("3 recurring expenses added: Office rent (Aug 01), Office rent (Sep 01), Office rent (Oct 01).", RecurringService.Summarize(made)!.Message);
        Assert.Empty(s.Recurring.GenerateDue());                 // twice: nothing new
        Assert.Equal(new DateOnly(2026, 11, 1), s.Recurring.NextDate(s.Recurring.Get(id)));
        Assert.Equal(0, s.Recurring.PreviewCount(Rent(s, new DateOnly(2026, 8, 1)), id));

        s.Expenses.Delete(made[1].ExpenseId);                    // deleted by hand: stays deleted
        day = new DateOnly(2026, 11, 3);
        Assert.Equal(new[] { new DateOnly(2026, 11, 1) }, s.Recurring.GenerateDue().Select(g => g.Date));
        Assert.Equal(3, s.Expenses.List().Count);

        Assert.Contains("are kept", s.Recurring.Delete(id).Message);
        Assert.Equal(3, s.Expenses.List().Count);
        Assert.All(s.Expenses.List(), e => Assert.Null(e.RecurringId));
    }

    [Fact]
    public void Bills_are_created_unpaid_paused_templates_create_nothing_and_resuming_skips_the_gap()
    {
        var day = new DateOnly(2026, 10, 2);
        var s = new Store(Fixture.TempPaths(), () => day);
        s.Settings.Set(SettingKeys.ExpensesEnabled, "true");
        long bill = s.Recurring.Create(Rent(s, new DateOnly(2026, 10, 1), RecurringMode.Bill)).Id;
        var e = s.Expenses.Get(s.Recurring.GenerateDue().Single().ExpenseId);
        Assert.Equal(InvoiceStatus.Unpaid, e.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), e.DueDate);
        Assert.Equal(bill, e.RecurringId);

        s.Recurring.ToggleActive(bill);                          // paused through Nov and Dec
        day = new DateOnly(2027, 1, 15);
        Assert.Empty(s.Recurring.GenerateDue());
        s.Recurring.ToggleActive(bill);                          // back on: Nov/Dec/Jan 1 skipped
        Assert.Empty(s.Recurring.GenerateDue());
        Assert.Equal(new DateOnly(2027, 2, 1), s.Recurring.NextDate(s.Recurring.Get(bill)));
        day = new DateOnly(2027, 2, 1);
        Assert.Single(s.Recurring.GenerateDue());

        long paused = s.Recurring.Create(Rent(s, new DateOnly(2026, 1, 1), active: false)).Id;
        Assert.Equal(0, s.Recurring.PreviewCount(Rent(s, new DateOnly(2026, 1, 1), active: false)));
        Assert.Empty(s.Recurring.GenerateDue());
        Assert.Null(s.Recurring.Get(paused).GeneratedThrough);
    }

    [Fact]
    public void A_recurring_expense_validates_its_fields()
    {
        var s = Fixture.FreshStore(Today);
        var e = Assert.Throws<ValidationException>(() => s.Recurring.Create(
            new RecurringDraft(null, null, "", -1, "weekly", null, 32, null, "paid", "barter", "", true)));
        Assert.Equal(new[] { "amount", "category_id", "day_of_month", "description", "frequency", "method", "start_date" }, e.Fields.Keys.OrderBy(k => k));
        Assert.Contains("end_date", Assert.Throws<ValidationException>(() => s.Recurring.Create(
            Rent(s, Today) with { EndDate = Today.AddDays(-1) })).Fields.Keys);
        Assert.Null(s.Recurring.Get(s.Recurring.Create(Rent(s, Today, RecurringMode.Bill) with { Method = "ach" }).Id).Method);
        Assert.Equal("Monthly on day 1", s.Recurring.List().Single().ScheduleLabel);
    }
}
