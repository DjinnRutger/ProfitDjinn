using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.5 service dates on invoice lines, and the billing period on recurring invoice lines.</summary>
public class ServiceDateTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static long Customer(Store s) => s.Customers.Create(new CustomerDraft("Acme", "", "", "", "", "", "", "", "", true)).Id;

    private static InvoiceDraft Draft(long customer, params InvoiceLineDraft[] lines) =>
        new(customer, "INV1001", Today, "", "", "", false, lines);

    [Fact]
    public void Service_dates_save_load_survive_an_edit_and_go_with_a_delete()
    {
        var s = Fixture.FreshStore(Today);
        long c = Customer(s);
        long id = s.Invoices.Create(Draft(c,
            new InvoiceLineDraft("Subscription", 1, 30, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            new InvoiceLineDraft("Setup", 1, 50))).Id;
        var inv = s.Invoices.Get(id);
        Assert.Equal("Service: 09/01/26 - 09/30/26", inv.Lines[0].ServiceText);
        Assert.Null(inv.Lines[1].ServiceText);

        s.Invoices.Update(id, Draft(c, new InvoiceLineDraft("Subscription", 1, 30, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))) with { InvoiceNumber = "INV1001" });
        var edited = Assert.Single(s.Invoices.Get(id).Lines);
        Assert.Equal(new DateOnly(2026, 10, 31), edited.ServiceEnd);
        Assert.Equal(1, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM invoice_line_service")));

        s.Invoices.Delete(id);
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM invoice_line_service")));
    }

    [Fact]
    public void An_end_before_the_start_is_refused_and_a_single_date_reads_alone()
    {
        var s = Fixture.FreshStore(Today);
        long c = Customer(s);
        var bad = Assert.Throws<ValidationException>(() => s.Invoices.Create(Draft(c,
            new InvoiceLineDraft("X", 1, 1, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)))));
        Assert.Contains("Line 1", bad.Message);
        Assert.Equal("Service: 09/15/26", ServiceDates.Text(new DateOnly(2026, 9, 15), null));
        Assert.Equal("Service: 09/15/26", ServiceDates.Text(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void Dates_left_behind_by_1x_are_cleaned_up_and_never_land_on_a_reused_line_id()
    {
        var paths = Fixture.TempPaths();
        var s = new Store(paths, () => Today);
        long c = Customer(s);
        long id = s.Invoices.Create(Draft(c, new InvoiceLineDraft("Dated", 1, 10, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)))).Id;
        // 1.x deletes the line without knowing about the side table.
        s.Database.Run(db => db.Execute("DELETE FROM invoice_lines"));
        s.Database.Run(db => db.Execute("INSERT INTO invoice_lines (invoice_id, description, quantity, amount) VALUES (@id, 'Undated', 1, 10)", new { id }));
        Assert.Null(Assert.Single(s.Invoices.Get(id).Lines).ServiceText);      // even before the clean-up
        var reopened = new Store(paths, () => Today);
        Assert.Null(Assert.Single(reopened.Invoices.Get(id).Lines).ServiceText);
        Assert.Equal(0, reopened.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM invoice_line_service")));
    }

    [Fact]
    public void Billing_the_period_fills_each_recurring_invoice_with_the_month_or_year_it_covers()
    {
        DateOnly day = new(2026, 11, 1);
        var s = new Store(Fixture.TempPaths(), () => day);
        long c = Customer(s);
        s.RecurringInvoices.Create(new RecurringInvoiceDraft(c, BillingInterval.Month, new DateOnly(2026, 11, 1), 1, null, "", "", "", true, new[]
        {
            new InvoiceLineDraft("Hosting", 1, 30, BillPeriod: true),
            new InvoiceLineDraft("Support", 1, 20),
        }));
        long yearly = s.RecurringInvoices.Create(new RecurringInvoiceDraft(c, BillingInterval.Year, new DateOnly(2026, 11, 15), 15, null, "", "", "", true, new[]
        {
            new InvoiceLineDraft("Domain", 1, 15, BillPeriod: true),
        })).Id;
        Assert.True(Assert.Single(s.RecurringInvoices.Get(yearly).Lines).BillPeriod);
        var preview = s.RecurringInvoices.Preview(yearly, new DateOnly(2026, 11, 15));
        Assert.Equal("Service: 11/15/26 - 11/14/27", preview.Lines[0].ServiceText);

        var made = Assert.Single(s.RecurringInvoices.GenerateDue().Created);
        var inv = s.Invoices.Get(made.InvoiceId);
        Assert.Equal("Service: 11/01/26 - 11/30/26", inv.Lines[0].ServiceText);
        Assert.Null(inv.Lines[1].ServiceText);
    }

    [Fact]
    public void The_pdf_renders_with_service_dates()
    {
        var s = Fixture.FreshStore(Today);
        long c = Customer(s);
        long id = s.Invoices.Create(Draft(c, new InvoiceLineDraft("Subscription", 1, 30, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)))).Id;
        byte[] pdf = InvoicePdf.Render(s.Invoices.Get(id), s.Settings.Company());
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
