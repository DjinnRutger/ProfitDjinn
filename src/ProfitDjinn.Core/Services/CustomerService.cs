using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>What the customer form submits.</summary>
public sealed record CustomerDraft(
    string Name, string Attn, string Address, string City, string State, string ZipCode,
    string Phone, string Email, string Notes, bool IsActive);

/// <summary>Customers. A port of 1.x app/blueprints/customers.py.</summary>
public sealed class CustomerService
{
    private readonly Database _db;

    public CustomerService(Database db) => _db = db;

    /// <summary>Active customers (or all), optionally filtered by name, sorted by name. Totals are loaded.</summary>
    public IReadOnlyList<Customer> List(string? search = null, bool includeInactive = false)
    {
        string q = (search ?? "").Trim();
        return _db.Run(db => Loader.Customers(db))
            .Where(c => includeInactive || c.IsActive)
            .Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Active customers for the invoice form's customer list, sorted by name.</summary>
    public IReadOnlyList<Customer> ActiveForPicker() =>
        _db.Run(db => db.Query<Customer>("SELECT * FROM customers WHERE is_active = 1 ORDER BY name").ToList());

    public Customer Get(long id) => _db.Run(db => Loader.Customer(db, id)) ?? throw NotFound();

    /// <summary>The customer's invoices for the history table, newest first.</summary>
    public IReadOnlyList<Invoice> InvoiceHistory(Customer customer) =>
        customer.Invoices.OrderByDescending(i => i.Date).ThenBy(i => i.Id).ToList();

    public Created Create(CustomerDraft draft)
    {
        var c = Normalize(draft);
        long id = _db.Run(db => db.ExecuteScalar<long>("""
            INSERT INTO customers (name, attn, address, city, state, zip_code, phone, email, notes, is_active, created_at)
            VALUES (@Name, @Attn, @Address, @City, @State, @ZipCode, @Phone, @Email, @Notes, @IsActive, @now);
            SELECT last_insert_rowid();
            """, new { c.Name, c.Attn, c.Address, c.City, c.State, c.ZipCode, c.Phone, c.Email, c.Notes, c.IsActive, now = SqlFormat.NowUtc() }));
        return new Created(id, Notice.Success($"Customer '{c.Name}' created."));
    }

    public Notice Update(long id, CustomerDraft draft)
    {
        var c = Normalize(draft);
        int n = _db.Run(db => db.Execute("""
            UPDATE customers SET name = @Name, attn = @Attn, address = @Address, city = @City, state = @State,
                zip_code = @ZipCode, phone = @Phone, email = @Email, notes = @Notes, is_active = @IsActive
            WHERE id = @id
            """, new { c.Name, c.Attn, c.Address, c.City, c.State, c.ZipCode, c.Phone, c.Email, c.Notes, c.IsActive, id }));
        if (n == 0) throw NotFound();
        return Notice.Success($"Customer '{c.Name}' updated.");
    }

    public Notice UpdateNotes(long id, string notes)
    {
        int n = _db.Run(db => db.Execute("UPDATE customers SET notes = @notes WHERE id = @id", new { notes = (notes ?? "").Trim(), id }));
        if (n == 0) throw NotFound();
        return Notice.Success("Notes saved.");
    }

    /// <summary>
    /// Deletes the customer with all their invoices (and those invoices' lines and payments)
    /// and their work order (and its lines), as 1.x's ORM cascades did.
    /// </summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string name = db.ExecuteScalar<string?>("SELECT name FROM customers WHERE id = @id", new { id }, tx) ?? throw NotFound();
        db.Execute("DELETE FROM invoice_lines WHERE invoice_id IN (SELECT id FROM invoices WHERE customer_id = @id)", new { id }, tx);
        db.Execute("DELETE FROM payments WHERE invoice_id IN (SELECT id FROM invoices WHERE customer_id = @id)", new { id }, tx);
        db.Execute("DELETE FROM work_order_lines WHERE work_order_id IN (SELECT id FROM work_orders WHERE customer_id = @id)", new { id }, tx);
        // Lines on another customer's tab that point at these invoices lose the link (SQLAlchemy nulled it).
        db.Execute("UPDATE work_order_lines SET invoice_id = NULL WHERE invoice_id IN (SELECT id FROM invoices WHERE customer_id = @id)", new { id }, tx);
        db.Execute("DELETE FROM invoices WHERE customer_id = @id", new { id }, tx);
        db.Execute("DELETE FROM work_orders WHERE customer_id = @id", new { id }, tx);
        db.Execute("DELETE FROM customers WHERE id = @id", new { id }, tx);
        return Notice.Warning($"Customer '{name}' deleted.");
    });

    /// <summary>1.x CustomerForm rules, then the clean-up its routes applied before saving.</summary>
    private static CustomerDraft Normalize(CustomerDraft d)
    {
        var checks = new Checks();
        checks.RequireText("name", d.Name, 200);
        checks.MaxLength("attn", d.Attn, 200);
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
            if (!email.Contains('@') || !email[(at + 1)..].Contains('.')) checks.Add("email", "Enter a valid email address.");
        }
        checks.ThrowIfAny();

        return new CustomerDraft(
            d.Name.Trim(), (d.Attn ?? "").Trim(), (d.Address ?? "").Trim(), (d.City ?? "").Trim(),
            (d.State ?? "").Trim().ToUpperInvariant(), (d.ZipCode ?? "").Trim(), (d.Phone ?? "").Trim(),
            email.ToLowerInvariant(), d.Notes ?? "", d.IsActive);
    }

    private static UserFacingException NotFound() => new("That customer no longer exists. They may have been deleted.");
}
