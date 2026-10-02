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
}
