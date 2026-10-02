using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>A category with how many expenses and recurring expenses use it.</summary>
public sealed record CategoryUsage(ExpenseCategory Category, int Expenses, int Recurring)
{
    public bool InUse => Expenses + Recurring > 0;
}

/// <summary>
/// 2.2. Expense categories. A starter list is seeded; the user can add, rename and hide them.
/// A category in use cannot be deleted, only hidden, so no expense loses its category.
/// </summary>
public sealed class ExpenseCategoryService
{
    public const int MaxName = 100;

    private readonly Database _db;

    public ExpenseCategoryService(Database db) => _db = db;

    /// <summary>Every category (or only the shown ones) in list order, with usage counts.</summary>
    public IReadOnlyList<CategoryUsage> List(bool includeHidden = true) => _db.Run(db =>
    {
        var expenses = db.Query<(long, int)>("SELECT category_id, COUNT(*) FROM expenses GROUP BY category_id").ToDictionary(r => r.Item1, r => r.Item2);
        var recurring = db.Query<(long, int)>("SELECT category_id, COUNT(*) FROM recurring_expenses GROUP BY category_id").ToDictionary(r => r.Item1, r => r.Item2);
        return Ordered(db)
            .Where(c => includeHidden || c.IsActive)
            .Select(c => new CategoryUsage(c, expenses.GetValueOrDefault(c.Id), recurring.GetValueOrDefault(c.Id)))
            .ToList();
    });

    /// <summary>Categories offered on the forms.</summary>
    public IReadOnlyList<ExpenseCategory> Active() => _db.Run(db => Ordered(db).Where(c => c.IsActive).ToList());

    public ExpenseCategory Get(long id) =>
        _db.Run(db => db.QuerySingleOrDefault<ExpenseCategory>("SELECT * FROM expense_categories WHERE id = @id", new { id })) ?? throw NotFound();

    /// <summary>New categories go before "Other" when it exists, else at the end.</summary>
    public Created Create(string name)
    {
        string clean = Validate(name, null);
        long id = _db.InTransaction((db, tx) =>
        {
            int order = db.ExecuteScalar<int?>("SELECT MAX(sort_order) FROM expense_categories WHERE name <> 'Other'", transaction: tx) + 1 ?? 0;
            db.Execute("UPDATE expense_categories SET sort_order = sort_order + 1 WHERE sort_order >= @order", new { order }, tx);
            return db.ExecuteScalar<long>(
                "INSERT INTO expense_categories (name, is_active, sort_order, created_at) VALUES (@clean, 1, @order, @now); SELECT last_insert_rowid();",
                new { clean, order, now = SqlFormat.NowUtc() }, tx);
        });
        return new Created(id, Notice.Success($"Category '{clean}' added."));
    }

    public Notice Rename(long id, string name)
    {
        var old = Get(id);
        string clean = Validate(name, id);
        _db.Run(db => db.Execute("UPDATE expense_categories SET name = @clean WHERE id = @id", new { clean, id }));
        return Notice.Success($"Category '{old.Name}' renamed to '{clean}'.");
    }

    public Notice ToggleActive(long id)
    {
        var c = Get(id);
        _db.Run(db => db.Execute("UPDATE expense_categories SET is_active = @active WHERE id = @id", new { active = !c.IsActive, id }));
        return Notice.Info(c.IsActive
            ? $"Category '{c.Name}' hidden. Existing expenses keep it; it is no longer offered on new ones."
            : $"Category '{c.Name}' shown again.");
    }

    public Notice Delete(long id)
    {
        var usage = List().FirstOrDefault(u => u.Category.Id == id) ?? throw NotFound();
        if (usage.InUse)
        {
            int n = usage.Expenses + usage.Recurring;
            throw new UserFacingException($"'{usage.Category.Name}' is used by {n} {Fmt.Plural(n, "expense")}, so it cannot be deleted. Hide it instead.");
        }
        _db.Run(db => db.Execute("DELETE FROM expense_categories WHERE id = @id", new { id }));
        return Notice.Warning($"Category '{usage.Category.Name}' deleted.");
    }

    private string Validate(string name, long? self)
    {
        var checks = new Checks();
        checks.RequireText("name", name, MaxName);
        checks.ThrowIfAny();
        string clean = name.Trim();
        bool taken = _db.Run(db => db.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM expense_categories WHERE name = @clean COLLATE NOCASE AND id <> @self)",
            new { clean, self = self ?? -1 }));
        if (taken)
        {
            checks.Add("name", $"A category named '{clean}' already exists.");
            checks.ThrowIfAny();
        }
        return clean;
    }

    private static List<ExpenseCategory> Ordered(Microsoft.Data.Sqlite.SqliteConnection db) =>
        db.Query<ExpenseCategory>("SELECT * FROM expense_categories ORDER BY sort_order, name COLLATE NOCASE").ToList();

    private static UserFacingException NotFound() => new("That category no longer exists. It may have been deleted.");
}
