using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>What the vendor form submits.</summary>
public sealed record VendorDraft(
    string Name, string Contact, string Address, string City, string State, string ZipCode,
    string Phone, string Email, long? DefaultCategoryId, string Notes, bool IsActive);

/// <summary>2.2. Vendors: who the business buys from. Shaped like <see cref="CustomerService"/>.</summary>
public sealed class VendorService
{
    private readonly Database _db;

    public VendorService(Database db) => _db = db;

    /// <summary>Active vendors (or all), optionally filtered by name, sorted by name. Totals are loaded.</summary>
    public IReadOnlyList<Vendor> List(string? search = null, bool includeInactive = false)
    {
        string q = (search ?? "").Trim();
        return _db.Run(db => Loader.Vendors(db))
            .Where(v => includeInactive || v.IsActive)
            .Where(v => q.Length == 0 || v.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Active vendors for the expense form, sorted by name.</summary>
    public IReadOnlyList<Vendor> ActiveForPicker() =>
        _db.Run(db => db.Query<Vendor>("SELECT * FROM vendors WHERE is_active = 1 ORDER BY name COLLATE NOCASE").ToList());

    public Vendor Get(long id) => _db.Run(db => Loader.Vendor(db, id)) ?? throw NotFound();

    /// <summary>The vendor's expenses for the history table, newest first.</summary>
    public IReadOnlyList<Expense> ExpenseHistory(Vendor vendor) =>
        vendor.Expenses.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToList();

    public Created Create(VendorDraft draft)
    {
        var v = Normalize(draft);
        long id = _db.Run(db => db.ExecuteScalar<long>("""
            INSERT INTO vendors (name, contact, address, city, state, zip_code, phone, email, default_category_id, notes, is_active, created_at)
            VALUES (@Name, @Contact, @Address, @City, @State, @ZipCode, @Phone, @Email, @DefaultCategoryId, @Notes, @IsActive, @now);
            SELECT last_insert_rowid();
            """, new { v.Name, v.Contact, v.Address, v.City, v.State, v.ZipCode, v.Phone, v.Email, v.DefaultCategoryId, v.Notes, v.IsActive, now = SqlFormat.NowUtc() }));
        return new Created(id, Notice.Success($"Vendor '{v.Name}' created."));
    }

    public Notice Update(long id, VendorDraft draft)
    {
        var v = Normalize(draft);
        int n = _db.Run(db => db.Execute("""
            UPDATE vendors SET name = @Name, contact = @Contact, address = @Address, city = @City, state = @State,
                zip_code = @ZipCode, phone = @Phone, email = @Email, default_category_id = @DefaultCategoryId,
                notes = @Notes, is_active = @IsActive
            WHERE id = @id
            """, new { v.Name, v.Contact, v.Address, v.City, v.State, v.ZipCode, v.Phone, v.Email, v.DefaultCategoryId, v.Notes, v.IsActive, id }));
        if (n == 0) throw NotFound();
        return Notice.Success($"Vendor '{v.Name}' updated.");
    }

    /// <summary>
    /// Only a vendor with no expenses and no recurring expenses can be deleted. Deleting one
    /// with history would leave expenses pointing at nothing; making it inactive keeps them.
    /// </summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string name = db.ExecuteScalar<string?>("SELECT name FROM vendors WHERE id = @id", new { id }, tx) ?? throw NotFound();
        int expenses = db.ExecuteScalar<int>("SELECT COUNT(*) FROM expenses WHERE vendor_id = @id", new { id }, tx);
        int recurring = db.ExecuteScalar<int>("SELECT COUNT(*) FROM recurring_expenses WHERE vendor_id = @id", new { id }, tx);
        if (expenses + recurring > 0)
        {
            string what = string.Join(" and ", new[]
            {
                expenses > 0 ? $"{expenses} {Fmt.Plural(expenses, "expense")}" : null,
                recurring > 0 ? $"{recurring} recurring {Fmt.Plural(recurring, "expense")}" : null,
            }.Where(s => s is not null));
            throw new UserFacingException($"{name} has {what}, so it cannot be deleted. Edit the vendor and make it inactive instead; that hides it and keeps the records.");
        }
        db.Execute("DELETE FROM vendors WHERE id = @id", new { id }, tx);
        return Notice.Warning($"Vendor '{name}' deleted.");
    });

    private VendorDraft Normalize(VendorDraft d)
    {
        var checks = new Checks();
        checks.RequireText("name", d.Name, 200);
        checks.MaxLength("contact", d.Contact, 200);
        checks.MaxLength("address", d.Address, 300);
        checks.MaxLength("city", d.City, 100);
        checks.MaxLength("state", d.State, 50);
        checks.MaxLength("zip_code", d.ZipCode, 20);
        checks.MaxLength("phone", d.Phone, 50);
        checks.MaxLength("email", d.Email, 200);
        string email = (d.Email ?? "").Trim();
        if (email.Length > 0)
        {
            int at = email.LastIndexOf('@');
            if (at < 0 || !email[(at + 1)..].Contains('.')) checks.Add("email", "Enter a valid email address.");
        }
        if (d.DefaultCategoryId is { } cat && !_db.Run(db => db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM expense_categories WHERE id = @cat)", new { cat })))
            checks.Add("default_category_id", "That category no longer exists. Choose another.");
        checks.ThrowIfAny();

        return new VendorDraft(
            d.Name.Trim(), (d.Contact ?? "").Trim(), (d.Address ?? "").Trim(), (d.City ?? "").Trim(),
            (d.State ?? "").Trim().ToUpperInvariant(), (d.ZipCode ?? "").Trim(), (d.Phone ?? "").Trim(),
            email.ToLowerInvariant(), d.DefaultCategoryId, d.Notes ?? "", d.IsActive);
    }

    private static UserFacingException NotFound() => new("That vendor no longer exists. It may have been deleted.");
}
