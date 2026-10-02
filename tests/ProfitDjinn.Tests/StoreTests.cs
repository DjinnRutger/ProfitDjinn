using Dapper;
using PdfSharp.Pdf.IO;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

public class StoreTests
{
    [Fact]
    public void A_fresh_database_is_seeded_like_1x_with_no_user_and_no_default_password()
    {
        var store = Fixture.FreshStore();
        store.Database.Run(db =>
        {
            Assert.Equal(32, db.ExecuteScalar<int>("SELECT COUNT(*) FROM permissions"));
            Assert.Equal(new[] { "Administrator", "Standard User" }, db.Query<string>("SELECT name FROM roles ORDER BY id"));
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM users"));
            Assert.Equal(0L, db.ExecuteScalar<long>("PRAGMA foreign_keys"));
        });
        Assert.Equal("ProfitDjinn", store.Settings.Get(SettingKeys.AppName));
        Assert.Equal("ProfitDjinn", store.Settings.Get(SettingKeys.FooterText));
        Assert.Equal("Your Name", store.Settings.Get(SettingKeys.CompanyName));
        Assert.False(store.Password.IsSet);
        Assert.Equal("light", store.Settings.Theme());
        Assert.Equal("INV1001", store.Invoices.NextNumber());
    }

    [Fact]
    public void Opening_twice_changes_nothing()
    {
        var paths = Fixture.TempPaths();
        _ = new Store(paths);
        int Count(Store s) => s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM settings"));
        Assert.Equal(Count(new Store(paths)), Count(new Store(paths)));
    }

    [Fact]
    public void Invoice_numbers_are_highest_plus_one_by_value()
    {
        Assert.Equal("JQ1000", Numbering.Next("JQ", new[] { "JQ998", "JQ999", "XX5000" }, "1001"));
        Assert.Equal("JQ1001", Numbering.Next("JQ", new[] { "JQ999", "JQ1000" }, "1001"));   // 1.x said JQ1000 again
        Assert.Equal("INV2018", Numbering.Next("INV", Array.Empty<string>(), "2018"));
        Assert.Equal("INV0007", Numbering.Next("INV", new[] { "inv0006" }, "1001"));
    }

    [Fact]
    public void App_password_is_off_until_set_and_then_required()
    {
        var store = Fixture.FreshStore();
        Assert.True(store.Password.Verify("anything"));
        Assert.Throws<UserFacingException>(() => store.Password.Set(null, "short", "short"));
        Assert.Throws<UserFacingException>(() => store.Password.Set(null, "long-enough", "different!"));
        store.Password.Set(null, "long-enough", "long-enough");
        Assert.True(store.Password.IsSet);
        Assert.True(store.Password.Verify("long-enough"));
        Assert.False(store.Password.Verify("wrong-password"));
        Assert.Throws<UserFacingException>(() => store.Password.Set("wrong-password", "another-one", "another-one"));
        store.Password.Remove("long-enough");
        Assert.False(store.Password.IsSet);
    }

    [Fact]
    public void Backup_and_restore_round_trip_with_a_safety_copy()
    {
        var store = Fixture.FreshStore();
        store.Customers.Create(new CustomerDraft("Kept", "", "", "", "", "", "", "", "", true));
        string backup = Path.Combine(store.Paths.DataFolder, "backup.db");
        store.Backups.Backup(backup);

        store.Customers.Create(new CustomerDraft("Added after backup", "", "", "", "", "", "", "", "", true));
        Assert.Equal(BackupCompatibility.Compatible, store.Backups.Analyze(backup).Status);

        string safety = store.Backups.Restore(backup, new DateTime(2026, 10, 2, 9, 30, 0));
        Assert.True(File.Exists(safety));
        Assert.Equal(new[] { "Kept" }, store.Customers.List().Select(c => c.Name));
    }

    [Fact]
    public void Restore_refuses_a_file_that_is_not_a_backup()
    {
        var store = Fixture.FreshStore();
        string bogus = Path.Combine(store.Paths.DataFolder, "notes.db");
        File.WriteAllText(bogus, "not a database");
        Assert.Throws<UserFacingException>(() => store.Backups.Analyze(bogus));
    }

    [Fact]
    public void Invoice_pdf_renders_and_long_invoices_break_onto_a_second_page()
    {
        var store = new Store(Fixture.CopyOf("parity.db"));
        var company = store.Settings.Company();

        byte[] one = InvoicePdf.Render(store.Invoices.Get(6), company);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(one, 0, 4));
        Assert.Equal(1, PdfReader.Open(new MemoryStream(one), PdfDocumentOpenMode.Import).PageCount);

        var big = store.Invoices.Get(1);
        big.Lines = Enumerable.Range(1, 60)
            .Select(i => new InvoiceLine { Description = $"Line {i} with a description long enough to wrap onto a second line in the column", Quantity = 1, Amount = i })
            .ToList();
        big.CreditApplied = 10;
        big.Notes = "Notes";
        byte[] many = InvoicePdf.Render(big, company);
        Assert.True(PdfReader.Open(new MemoryStream(many), PdfDocumentOpenMode.Import).PageCount >= 2);
    }
}
