using Dapper;
using PdfSharp.Pdf.IO;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.9: purchase orders, the Vendors switch, the letterhead, and Revenue's top ten.</summary>
public class PurchaseOrderTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    /// <summary>A 4 x 2 RGBA PNG, standing in for a logo.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAYAAAB/qH1jAAAAEklEQVR4nGM4ISf3HxkzoAsAAA81EBmbqAf0AAAAAElFTkSuQmCC");

    private static Store Open()
    {
        var s = Fixture.FreshStore(Today);
        s.Settings.Set(SettingKeys.PurchaseOrdersEnabled, "true");
        return s;
    }

    private static long Vendor(Store s, string name = "Glass Supply Co", bool active = true) =>
        s.Vendors.Create(new VendorDraft(name, "Pat", "100 Industrial Rd", "Omaha", "NE", "68102", "402-555-0100", "orders@example.com", null, "", active)).Id;

    private static PurchaseOrderDraft Draft(Store s, long vendor, params PurchaseOrderLineDraft[] lines) =>
        new(vendor, s.PurchaseOrders.NextNumber(), Today, "", lines.Length > 0 ? lines : new[] { new PurchaseOrderLineDraft("Clear glass 36 x 60", 2, 45.5) });

    // ------------------------------------------------------------------ switches

    [Fact]
    public void Vendors_are_on_while_expenses_or_purchase_orders_are()
    {
        var s = Fixture.FreshStore(Today);
        Assert.False(s.PurchaseOrders.Enabled);
        Assert.False(s.Vendors.Enabled);

        s.Settings.Set(SettingKeys.ExpensesEnabled, "true");
        Assert.True(s.Vendors.Enabled);   // a pre-2.9 database with Expenses on keeps its Vendors
        s.Settings.Set(SettingKeys.ExpensesEnabled, "false");
        s.Settings.Set(SettingKeys.PurchaseOrdersEnabled, "true");
        Assert.True(s.Vendors.Enabled);
        s.Settings.Set(SettingKeys.PurchaseOrdersEnabled, "false");
        Assert.False(s.Vendors.Enabled);
        s.Settings.Set(SettingKeys.VendorsEnabled, "true");
        Assert.True(s.Vendors.Enabled);
    }

    // ------------------------------------------------------------------ numbering

    [Fact]
    public void Numbers_follow_the_prefix_and_starting_number_settings()
    {
        var s = Open();
        long v = Vendor(s);
        Assert.Equal("PO1001", s.PurchaseOrders.NextNumber());
        s.PurchaseOrders.Create(Draft(s, v));
        Assert.Equal("PO1002", s.PurchaseOrders.NextNumber());

        // A different prefix starts its own sequence from the starting number.
        s.Settings.Set(SettingKeys.PurchaseOrderPrefix, "");
        s.Settings.Set(SettingKeys.PurchaseOrderNextNumber, "1");
        Assert.Equal("0001", s.PurchaseOrders.NextNumber());
        s.PurchaseOrders.Create(Draft(s, v));
        Assert.Equal("0002", s.PurchaseOrders.NextNumber());
    }

    [Fact]
    public void A_number_already_used_is_refused()
    {
        var s = Open();
        long v = Vendor(s);
        s.PurchaseOrders.Create(Draft(s, v) with { PoNumber = "po77" });
        var ex = Assert.Throws<ValidationException>(() => s.PurchaseOrders.Create(Draft(s, v) with { PoNumber = "PO77" }));
        Assert.True(ex.Fields.ContainsKey("po_number"));
    }

    // ------------------------------------------------------------------ lines

    [Fact]
    public void Lines_keep_quantity_price_and_order_and_unused_rows_are_dropped()
    {
        var s = Open();
        long v = Vendor(s);
        var created = s.PurchaseOrders.Create(Draft(s, v,
            new PurchaseOrderLineDraft("  Tempered 1/4 in  ", 3, 107.6),
            new PurchaseOrderLineDraft("", 1, 0),
            new PurchaseOrderLineDraft("Glazing tape", 1, 12.25)));
        var o = s.PurchaseOrders.Get(created.Id);
        Assert.Equal(new[] { "Tempered 1/4 in", "Glazing tape" }, o.Lines.Select(l => l.Description));
        Assert.Equal(3, o.Lines[0].Quantity);
        Assert.Equal(107.6, o.Lines[0].UnitPrice);
        Assert.Equal(3 * 107.6, o.Lines[0].Amount);
        Assert.Equal(PurchaseOrderStatus.Open, o.Status);
        Assert.Equal("Glass Supply Co", o.VendorName);

        s.PurchaseOrders.Update(o.Id, Draft(s, v, new PurchaseOrderLineDraft("Screen kit", 4, 5)) with { PoNumber = o.PoNumber, Notes = "Deliver Tuesday" });
        o = s.PurchaseOrders.Get(o.Id);
        Assert.Single(o.Lines);
        Assert.Equal(20, o.Total);
        Assert.Equal("Deliver Tuesday", o.Notes);
    }

    [Fact]
    public void A_priced_line_needs_a_description_and_a_PO_needs_a_line()
    {
        var s = Open();
        long v = Vendor(s);
        var ex = Assert.Throws<ValidationException>(() => s.PurchaseOrders.Create(Draft(s, v, new PurchaseOrderLineDraft(" ", 1, 10))));
        Assert.Contains("no description", ex.Fields["lines"]);
        Assert.Throws<UserFacingException>(() => s.PurchaseOrders.Create(Draft(s, v, new PurchaseOrderLineDraft("", 0, 0))));
    }

    [Fact]
    public void A_line_builder_row_reads_numbers_like_an_invoice_line()
    {
        var line = PurchaseOrderLineDraft.FromRow(new InvoiceRowInput("Glass", "2", "$1,250.50"));
        Assert.Equal(("Glass", 2.0), (line.Description, line.Quantity));
        Assert.Equal(InvoiceRows.Price("$1,250.50"), line.UnitPrice);
    }

    [Fact]
    public void An_inactive_vendor_cannot_get_a_new_PO_but_keeps_an_existing_one()
    {
        var s = Open();
        long v = Vendor(s);
        long id = s.PurchaseOrders.Create(Draft(s, v)).Id;
        var vendor = s.Vendors.Get(v);
        s.Vendors.Update(v, new VendorDraft(vendor.Name, "", "", "", "", "", "", "", null, "", false));

        Assert.Throws<ValidationException>(() => s.PurchaseOrders.Create(Draft(s, v)));
        var o = s.PurchaseOrders.Get(id);
        s.PurchaseOrders.Update(id, Draft(s, v) with { PoNumber = o.PoNumber, Notes = "still fine" });
    }

    // ------------------------------------------------------------------ status, delete, vendor

    [Fact]
    public void Received_is_dated_today_and_reopening_clears_it()
    {
        var s = Open();
        long id = s.PurchaseOrders.Create(Draft(s, Vendor(s))).Id;
        s.PurchaseOrders.SetStatus(id, PurchaseOrderStatus.Received);
        Assert.Equal(Today, s.PurchaseOrders.Get(id).ReceivedDate);
        Assert.Equal(0, s.PurchaseOrders.OpenSummary().Count);

        s.PurchaseOrders.SetStatus(id, PurchaseOrderStatus.Open);
        Assert.Null(s.PurchaseOrders.Get(id).ReceivedDate);
        s.PurchaseOrders.SetStatus(id, PurchaseOrderStatus.Cancelled);
        Assert.Equal("Cancelled", s.PurchaseOrders.Get(id).StatusLabel);
        Assert.Single(s.PurchaseOrders.List(PurchaseOrderStatus.Cancelled));
        Assert.Empty(s.PurchaseOrders.List(PurchaseOrderStatus.Open));
    }

    [Fact]
    public void A_vendor_lists_its_POs_and_cannot_be_deleted_while_it_has_any()
    {
        var s = Open();
        long v = Vendor(s);
        long other = Vendor(s, "Hardware Barn");
        long first = s.PurchaseOrders.Create(Draft(s, v)).Id;
        s.PurchaseOrders.Create(Draft(s, v) with { Date = Today.AddDays(1) });
        s.PurchaseOrders.Create(Draft(s, other));

        var history = s.Vendors.PurchaseOrderHistory(s.Vendors.Get(v));
        Assert.Equal(2, history.Count);
        Assert.Equal(Today.AddDays(1), history[0].Date);
        Assert.Single(s.PurchaseOrders.List(search: "hardware"));

        var ex = Assert.Throws<UserFacingException>(() => s.Vendors.Delete(v));
        Assert.Contains("2 purchase orders", ex.Message);

        s.PurchaseOrders.Delete(first);
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM purchase_order_lines WHERE purchase_order_id = @first", new { first })));
    }

    // ------------------------------------------------------------------ letterhead and PDFs

    [Fact]
    public void Company_carries_the_letterhead_and_reads_the_logo_file()
    {
        var s = Open();
        Assert.False(s.Settings.Company().HasLetterhead);
        s.Settings.Set(SettingKeys.CompanyServices, "Glass replacement\r\n\r\n  Shower doors  ");
        s.Settings.Set(SettingKeys.CompanyContactLine, " Jane Smith, Owner ");
        var c = s.Settings.Company();
        Assert.Equal(new[] { "Glass replacement", "Shower doors" }, c.ServiceList);
        Assert.Equal("Jane Smith, Owner", c.ContactLine);
        Assert.Null(c.Logo);

        Directory.CreateDirectory(s.Paths.BrandingFolder);
        File.WriteAllBytes(s.Paths.DocumentLogoPath, TinyPng);
        Assert.Null(s.Settings.Company().Logo);   // the setting says whether a logo is in use
        s.Settings.Set(SettingKeys.DocumentLogo, "document_logo.png");
        Assert.Equal(TinyPng, s.Settings.Company().Logo);
        File.Delete(s.Paths.DocumentLogoPath);
        Assert.Null(s.Settings.Company().Logo);   // a missing file prints no logo rather than failing
    }

    [Fact]
    public void PO_pdf_renders_with_and_without_a_logo_and_long_orders_run_onto_more_pages()
    {
        var s = Open();
        long v = Vendor(s);
        long small = s.PurchaseOrders.Create(Draft(s, v)).Id;
        var lines = Enumerable.Range(1, 40)
            .Select(i => new PurchaseOrderLineDraft($"Line {i}, an insulated glass unit with a description long enough to wrap", 1, i))
            .ToArray();
        long big = s.PurchaseOrders.Create(Draft(s, v, lines) with { Notes = "Deliver before 10am." }).Id;

        byte[] plain = PurchaseOrderPdf.Render(s.PurchaseOrders.Get(small), s.Settings.Company());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(plain, 0, 4));
        Assert.Equal(1, PdfReader.Open(new MemoryStream(plain), PdfDocumentOpenMode.Import).PageCount);

        Directory.CreateDirectory(s.Paths.BrandingFolder);
        File.WriteAllBytes(s.Paths.DocumentLogoPath, TinyPng);
        s.Settings.Set(SettingKeys.DocumentLogo, "document_logo.png");
        s.Settings.Set(SettingKeys.CompanyServices, "One\nTwo\nThree\nFour");
        byte[] many = PurchaseOrderPdf.Render(s.PurchaseOrders.Get(big), s.Settings.Company());
        Assert.True(PdfReader.Open(new MemoryStream(many), PdfDocumentOpenMode.Import).PageCount >= 2);
    }

    [Fact]
    public void Invoice_pdf_uses_the_letterhead_once_one_is_set()
    {
        var s = Open();
        long c = s.Customers.Create(new CustomerDraft("Client", "", "", "", "", "", "", "", "", true)).Id;
        long id = s.Invoices.Create(new InvoiceDraft(c, s.Invoices.NextNumber(), Today, "", "", "", false, new[] { new InvoiceLineDraft("Work", 1, 100) })).Id;
        byte[] before = InvoicePdf.Render(s.Invoices.Get(id), s.Settings.Company());

        Directory.CreateDirectory(s.Paths.BrandingFolder);
        File.WriteAllBytes(s.Paths.DocumentLogoPath, TinyPng);
        s.Settings.Set(SettingKeys.DocumentLogo, "document_logo.png");
        s.Settings.Set(SettingKeys.CompanyContactLine, "Jane Smith, Owner");
        byte[] after = InvoicePdf.Render(s.Invoices.Get(id), s.Settings.Company());
        Assert.Equal(1, PdfReader.Open(new MemoryStream(after), PdfDocumentOpenMode.Import).PageCount);
        Assert.True(after.Length > before.Length);   // the logo is embedded
    }

    // ------------------------------------------------------------------ Revenue

    [Fact]
    public void Top_customers_are_the_ten_who_paid_most_in_the_last_12_months()
    {
        var s = Fixture.FreshStore(Today);
        void Paid(string name, double amount, DateOnly date)
        {
            long c = s.Customers.List().FirstOrDefault(x => x.Name == name)?.Id
                ?? s.Customers.Create(new CustomerDraft(name, "", "", "", "", "", "", "", "", true)).Id;
            long inv = s.Invoices.Create(new InvoiceDraft(c, s.Invoices.NextNumber(), date, "", "", "", false, new[] { new InvoiceLineDraft("Work", 1, amount) })).Id;
            s.Invoices.RecordPayment(inv, amount, "cash", null, date, null);
        }
        for (int i = 1; i <= 12; i++) Paid($"Customer {i:00}", i * 100, Today.AddMonths(-1));
        Paid("Old Big Spender", 50_000, Today.AddMonths(-12));   // exactly 12 months back: outside the window
        Paid("Customer 01", 2_000, Today);                       // today counts

        var top = s.Reports.Revenue(Today.Year - 1).TopCustomersLast12Months;
        Assert.Equal(10, top.Count);
        Assert.Equal("Customer 01", top[0].Name);
        Assert.Equal(2_100, top[0].Collected);
        Assert.DoesNotContain(top, t => t.Name == "Old Big Spender");
        Assert.Equal("Customer 12", top[1].Name);
        Assert.DoesNotContain(top, t => t.Name is "Customer 02" or "Customer 03");
        double window = 2_100 + Enumerable.Range(2, 11).Sum(i => i * 100);
        Assert.Equal(2_100 / window * 100, top[0].Share, 9);
    }
}
