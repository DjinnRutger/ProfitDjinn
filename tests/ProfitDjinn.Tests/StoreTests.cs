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
        Assert.Equal("", store.Settings.Get(SettingKeys.CompanyName));         // 2.5: hints in Settings, not sample text
        Assert.Equal("", store.Settings.Get(SettingKeys.InvoiceTerm2));
        Assert.Equal("Payment Terms: Due within 30 days", store.Settings.Get(SettingKeys.InvoiceTerm1));
        Assert.True(store.Settings.GetBool(SettingKeys.WorkOrdersEnabled));
        Assert.False(store.Password.IsSet);
        Assert.Equal("light", store.Settings.Theme());
        Assert.Equal("INV1001", store.Invoices.NextNumber());
    }

    [Fact]
    public void Old_sample_business_values_are_cleared_but_real_ones_are_kept()
    {
        var paths = Fixture.TempPaths();
        var store = new Store(paths);
        store.Settings.Set(SettingKeys.CompanyName, "Your Name");
        store.Settings.Set(SettingKeys.CompanyCity, "Anytown");
        store.Settings.Set(SettingKeys.InvoiceTerm2, "Make all checks payable to Your Name");
        store.Settings.Set(SettingKeys.CompanyAddress, "77 Real Road");
        store.Settings.Set(SettingKeys.CompanyState, "MT");
        var reopened = new Store(paths);
        Assert.Equal("", reopened.Settings.Get(SettingKeys.CompanyName));
        Assert.Equal("", reopened.Settings.Get(SettingKeys.CompanyCity));
        Assert.Equal("", reopened.Settings.Get(SettingKeys.InvoiceTerm2));
        Assert.Equal("77 Real Road", reopened.Settings.Get(SettingKeys.CompanyAddress));
        Assert.Equal("MT", reopened.Settings.Get(SettingKeys.CompanyState));
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
    public void Backup_reminder_is_on_every_7_days_and_waits_on_a_new_install()
    {
        var day = new DateOnly(2026, 10, 2);
        var store = new Store(Fixture.TempPaths(), () => day);
        var reminder = store.BackupReminder;
        Assert.True(reminder.Enabled);
        Assert.Equal(7, reminder.Days);

        Assert.False(reminder.IsDue());                        // first start: countdown begins, no popup
        Assert.Equal("2026-10-02", store.Settings.Get(SettingKeys.BackupReminderLast));
        day = day.AddDays(6);
        Assert.False(reminder.IsDue());
        day = day.AddDays(1);
        Assert.True(reminder.IsDue());                         // 7 days on
        Assert.True(reminder.IsDue());                         // still due until answered

        reminder.Restart();                                    // answered (yes or not now) or a backup made
        Assert.False(reminder.IsDue());
        day = day.AddDays(7);
        Assert.True(reminder.IsDue());

        store.Settings.Set(SettingKeys.BackupReminderEnabled, "false");
        Assert.False(reminder.IsDue());
        store.Settings.Set(SettingKeys.BackupReminderEnabled, "true");
        store.Settings.Set(SettingKeys.BackupReminderDays, "30");
        Assert.False(reminder.IsDue());
        day = day.AddDays(30);
        Assert.True(reminder.IsDue());
    }

    [Fact]
    public void Backup_reminder_days_must_be_1_to_365_and_a_bad_saved_value_falls_back_to_7()
    {
        Assert.Null(BackupReminder.Validate("1"));
        Assert.Null(BackupReminder.Validate(" 365 "));
        foreach (string bad in new[] { "", "0", "366", "-3", "2.5", "seven" })
            Assert.NotNull(BackupReminder.Validate(bad));

        var store = Fixture.FreshStore();
        store.Settings.Set(SettingKeys.BackupReminderDays, "abc");
        Assert.Equal(7, store.BackupReminder.Days);
        store.Settings.Set(SettingKeys.BackupReminderLast, "not a date");
        Assert.False(store.BackupReminder.IsDue());             // treated as no countdown: starts one
    }

    [Fact]
    public void A_new_version_saves_a_copy_before_upgrading_and_keeps_the_newest_five()
    {
        string[] Copies(AppPaths p) => Directory.Exists(p.UpgradeBackupFolder)
            ? Directory.GetFiles(p.UpgradeBackupFolder, "pre-upgrade_*.db").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()!
            : Array.Empty<string>();

        // Brand-new install: nothing to save.
        var fresh = Fixture.TempPaths();
        _ = new Store(fresh, appVersion: "2.1.0");
        Assert.Empty(Copies(fresh));
        _ = new Store(fresh, appVersion: "2.1.0");             // same version again: still nothing
        Assert.Empty(Copies(fresh));

        // A 1.x database has never recorded a version: it is copied, untouched, before the upgrade.
        var paths = Fixture.CopyOf("parity.db");
        var store = new Store(paths, appVersion: "2.1.0");
        var first = Assert.Single(Copies(paths));
        Assert.Contains("_from-older_to-2.1.0.db", first);
        Assert.Equal("2.1.0", store.Settings.Get(SettingKeys.DatabaseAppVersion));
        long InvoiceCount(string file)
        {
            using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            return c.ExecuteScalar<long>("SELECT COUNT(*) FROM invoices");
        }
        string firstPath = Path.Combine(paths.UpgradeBackupFolder, first);
        Assert.Equal(InvoiceCount(Fixture.PathOf("parity.db")), InvoiceCount(firstPath));
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={firstPath};Mode=ReadOnly;Pooling=False"))
        {
            c.Open();                                           // taken before the upgrade: none of the 2.x settings rows yet
            Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM settings WHERE \"key\" IN ('db_app_version', 'backup_reminder_enabled')"));
        }

        _ = new Store(paths, appVersion: "2.1.0");             // reopening the same version saves nothing
        Assert.Single(Copies(paths));

        var at = DateTime.Now.AddMinutes(1);                    // six more upgrades: only the newest five stay
        for (int i = 2; i <= 7; i++)
            Assert.NotNull(UpgradeBackup.SaveIfVersionChanged(store.Database, paths, $"2.{i}.0", at.AddMinutes(i)));
        var kept = Copies(paths);
        Assert.Equal(UpgradeBackup.Keep, kept.Length);
        Assert.Contains("_from-2.1.0_to-2.7.0.db", kept[^1]);
        Assert.DoesNotContain(kept, f => f.Contains("from-older"));
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
        // For a side-by-side check against 1.x's PDF of the same invoice.
        if (Environment.GetEnvironmentVariable("PROFITDJINN_PDF_OUT") is { Length: > 0 } outDir)
        {
            File.WriteAllBytes(Path.Combine(outDir, "inv6-2.0.pdf"), one);
            File.WriteAllBytes(Path.Combine(outDir, "inv5-2.0.pdf"), InvoicePdf.Render(store.Invoices.Get(5), company));
        }
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
