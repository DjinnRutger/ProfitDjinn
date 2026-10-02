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
}
