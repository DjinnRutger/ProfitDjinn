using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

public enum InvoiceFilter { All, Unpaid, Paid }

/// <summary>What the invoice form submits.</summary>
public sealed record InvoiceDraft(
    long? CustomerId,
    string InvoiceNumber,
    DateOnly? Date,
    string Notes,
    string Term1,
    string Term2,
    bool Paid,
    IReadOnlyList<InvoiceLineDraft> Lines);

/// <summary>Invoices and payments. A port of 1.x app/blueprints/invoices.py.</summary>
public sealed class InvoiceService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public InvoiceService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// The invoice list. Unpaid means the paid flag is off, so it includes partial payments.
    /// Search matches the number or the customer name, ignoring case. Newest first.
    /// </summary>
    public IReadOnlyList<Invoice> List(InvoiceFilter filter = InvoiceFilter.All, string? search = null)
    {
        var all = _db.Run(db => Loader.Invoices(db));
        string q = (search ?? "").Trim();
        return all
            .Where(i => filter switch
            {
                InvoiceFilter.Paid => i.Paid,
                InvoiceFilter.Unpaid => !i.Paid,
                _ => true,
            })
            .Where(i => q.Length == 0
                || i.InvoiceNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (i.Customer?.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.InvoiceNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>"M with balance ($X)" in the list header: across all invoices, not just the ones shown.</summary>
    public (int Count, double Total) UnpaidSummary()
    {
        var owing = _db.Run(db => Loader.Invoices(db, withCustomers: false)).Where(i => i.BalanceDue > 0).ToList();
        return (owing.Count, PyMath.Sum(owing, i => i.BalanceDue));
    }

    public Invoice Get(long id)
    {
        var invoice = _db.Run(db => Loader.Invoice(db, id)) ?? throw NotFound();
        return invoice;
    }

    /// <summary>The customer's unused account credit, rounded to cents as the payment dialog shows it.</summary>
    public double AvailableCredit(long customerId)
    {
        var customer = _db.Run(db => Loader.Customer(db, customerId));
        return customer is null ? 0.0 : PyMath.Round(customer.AccountCredit, 2);
    }

    public string NextNumber() => _db.Run(db => NextNumber(db, null));

    internal string NextNumber(SqliteConnection db, SqliteTransaction? tx) => Numbering.Next(
        _settings.Get(SettingKeys.InvoicePrefix, "INV"),
        db.Query<string>("SELECT invoice_number FROM invoices", transaction: tx),
        _settings.Get(SettingKeys.InvoiceNextNumber, "1001"));

    /// <summary>A new invoice's starting values: next number, today, the default terms.</summary>
    public InvoiceDraft NewDraft(long? customerId = null) => new(
        customerId, NextNumber(), _today(), "",
        _settings.Get(SettingKeys.InvoiceTerm1, "Payment Terms: Due within 30 days"),
        _settings.Get(SettingKeys.InvoiceTerm2, "Make all checks payable to Your Name"),
        false, Array.Empty<InvoiceLineDraft>());

    // ------------------------------------------------------------------ create / edit

    public Created Create(InvoiceDraft draft)
    {
        string number = Validate(draft, editingId: null);
        return _db.InTransaction((db, tx) =>
        {
            EnsureUniqueNumber(db, tx, number, draft.InvoiceNumber, exceptId: null);
            RequireCustomer(db, tx, draft.CustomerId!.Value, mustBeActive: true);
            long id = db.ExecuteScalar<long>("""
                INSERT INTO invoices (invoice_number, customer_id, date, notes, term1, term2, paid, paid_date, credit_applied, created_at)
                VALUES (@number, @customer, @date, @notes, @term1, @term2, @paid, @paidDate, 0, @now);
                SELECT last_insert_rowid();
                """, new
            {
                number,
                customer = draft.CustomerId,
                date = draft.Date!.Value,
                notes = draft.Notes ?? "",
                term1 = draft.Term1 ?? "",
                term2 = draft.Term2 ?? "",
                paid = draft.Paid,
                paidDate = draft.Paid ? _today() : (DateOnly?)null,
                now = SqlFormat.NowUtc(),
            }, tx);
            InsertLines(db, tx, id, draft.Lines, skipBlank: false, roundAmounts: false);
            return new Created(id, Notice.Success($"Invoice {number} created."));
        });
    }

    public Notice Update(long id, InvoiceDraft draft)
    {
        string number = Validate(draft, editingId: id);
        return _db.InTransaction((db, tx) =>
        {
            var invoice = Loader.Invoice(db, id, tx) ?? throw NotFound();
            EnsureUniqueNumber(db, tx, number, draft.InvoiceNumber, exceptId: id);

            long newCustomer = draft.CustomerId!.Value;
            // The current customer is allowed even if made inactive since (fixed in 2.0).
            RequireCustomer(db, tx, newCustomer, mustBeActive: newCustomer != invoice.CustomerId);

            bool fromWorkOrder = db.ExecuteScalar<bool>(
                "SELECT EXISTS (SELECT 1 FROM work_order_lines WHERE invoice_id = @id)", new { id }, tx);
            if (fromWorkOrder && newCustomer != invoice.CustomerId)
                throw new UserFacingException(
                    $"This invoice was created from {invoice.Customer?.Name}'s work order, so it can't be reassigned " +
                    "to a different customer. Delete the invoice instead — the work returns to the tab and you can re-bill it.");

            invoice.InvoiceNumber = number;
            invoice.CustomerId = newCustomer;
            invoice.Date = draft.Date!.Value;
            invoice.Notes = draft.Notes ?? "";
            invoice.Term1 = draft.Term1 ?? "";
            invoice.Term2 = draft.Term2 ?? "";
            if (draft.Paid && !invoice.Paid) invoice.PaidDate = _today();
            else if (!draft.Paid) invoice.PaidDate = null;
            invoice.Paid = draft.Paid;

            db.Execute("DELETE FROM invoice_lines WHERE invoice_id = @id", new { id }, tx);
            InsertLines(db, tx, id, draft.Lines, skipBlank: false, roundAmounts: false);
            invoice.Lines = draft.Lines.Select(l => new InvoiceLine { Description = l.Description, Quantity = l.Quantity, Amount = l.Amount }).ToList();

            if (invoice.CreditApplied > invoice.Total) invoice.CreditApplied = PyMath.Round(invoice.Total, 2);
            if (!draft.Paid) invoice.RecalcPaidStatus(_today());

            SaveHeader(db, tx, invoice);
            return Notice.Success($"Invoice {number} updated.");
        });
    }

    /// <summary>Deletes the invoice. Work billed onto it goes back to the customer's tab, ready to bill again.</summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string number = db.ExecuteScalar<string?>("SELECT invoice_number FROM invoices WHERE id = @id", new { id }, tx)
            ?? throw NotFound();
        int restored = db.Execute(
            "UPDATE work_order_lines SET status = 'completed', invoice_id = NULL, billed_at = NULL WHERE invoice_id = @id",
            new { id }, tx);
        db.Execute("DELETE FROM invoice_lines WHERE invoice_id = @id", new { id }, tx);
        db.Execute("DELETE FROM payments WHERE invoice_id = @id", new { id }, tx);
        db.Execute("DELETE FROM invoices WHERE id = @id", new { id }, tx);
        return restored > 0
            ? Notice.Warning($"Invoice {number} deleted. {restored} work order {Fmt.Plural(restored, "line")} returned to the tab, ready to bill again.")
            : Notice.Warning($"Invoice {number} deleted.");
    });

    // ------------------------------------------------------------------ payments

    /// <summary>
    /// Records a payment. The "account_credit" method applies the customer's unused credit
    /// against this invoice instead of recording money: it raises the invoice's applied credit
    /// so the customer's credit balance draws down.
    /// </summary>
    public Notice RecordPayment(long invoiceId, double amount, string method, string? checkNumber, DateOnly? date, string? notes)
    {
        if (!(amount > 0)) throw new UserFacingException("Payment amount must be greater than zero.");
        method = string.IsNullOrEmpty(method) ? "cash" : method;

        return _db.InTransaction((db, tx) =>
        {
            var invoice = Loader.Invoice(db, invoiceId, tx) ?? throw NotFound();

            if (method == PaymentMethods.AccountCredit)
            {
                var customer = Loader.Customer(db, invoice.CustomerId, tx);
                double available = customer?.AccountCredit ?? 0.0;
                double applied = PyMath.Round(Math.Min(Math.Min(amount, available), invoice.BalanceDue), 2);
                if (applied <= 0) return Notice.Warning("No account credit is available to apply.");

                invoice.CreditApplied = PyMath.Round(invoice.CreditApplied + applied, 2);
                invoice.RecalcPaidStatus(_today());
                SaveHeader(db, tx, invoice);
                return invoice.BalanceDue <= 0
                    ? Notice.Success($"${Fmt.F2(applied)} account credit applied. Invoice paid in full.")
                    : Notice.Info($"${Fmt.F2(applied)} account credit applied. Balance remaining: ${Fmt.F2(invoice.BalanceDue)}.");
            }

            DateOnly paymentDate = date ?? _today();
            var payment = new Payment
            {
                InvoiceId = invoiceId,
                CustomerId = invoice.CustomerId,
                Amount = amount,
                Method = method,
                CheckNumber = method == PaymentMethods.Check ? (checkNumber ?? "").Trim() : "",
                Date = paymentDate,
                Notes = (notes ?? "").Trim(),
            };
            db.Execute("""
                INSERT INTO payments (invoice_id, customer_id, amount, method, check_number, date, notes, created_at)
                VALUES (@InvoiceId, @CustomerId, @Amount, @Method, @CheckNumber, @Date, @Notes, @now)
                """, new { payment.InvoiceId, payment.CustomerId, payment.Amount, payment.Method, payment.CheckNumber, payment.Date, payment.Notes, now = SqlFormat.NowUtc() }, tx);
            invoice.Payments = db.Query<Payment>("SELECT * FROM payments WHERE invoice_id = @invoiceId ORDER BY date, id", new { invoiceId }, tx).ToList();

            double newPaid = PyMath.Sum(invoice.Payments, p => p.Amount);
            Notice notice;
            if (newPaid >= invoice.NetTotal)
            {
                invoice.Paid = true;
                invoice.PaidDate ??= paymentDate;
                double credit = newPaid - invoice.NetTotal;
                notice = credit > 0
                    ? Notice.Success($"Payment of ${Fmt.F2(amount)} recorded. Invoice paid in full. ${Fmt.F2(credit)} credit applied to account.")
                    : Notice.Success($"Payment of ${Fmt.F2(amount)} recorded. Invoice paid in full.");
            }
            else
            {
                invoice.Paid = false;
                invoice.PaidDate = null;
                notice = Notice.Info($"Partial payment of ${Fmt.F2(amount)} recorded. Balance remaining: ${Fmt.F2(invoice.NetTotal - newPaid)}.");
            }
            SaveHeader(db, tx, invoice);
            return notice;
        });
    }

    public Notice DeletePayment(long invoiceId, long paymentId) => _db.InTransaction((db, tx) =>
    {
        var invoice = Loader.Invoice(db, invoiceId, tx) ?? throw NotFound();
        if (db.Execute("DELETE FROM payments WHERE id = @paymentId AND invoice_id = @invoiceId", new { paymentId, invoiceId }, tx) == 0)
            throw new UserFacingException("That payment is not on this invoice. It may already have been deleted.");

        double remaining = PyMath.Sum(invoice.Payments.Where(p => p.Id != paymentId), p => p.Amount);
        if (remaining >= invoice.NetTotal) invoice.Paid = true;
        else
        {
            invoice.Paid = false;
            invoice.PaidDate = null;
        }
        SaveHeader(db, tx, invoice);
        return Notice.Warning("Payment deleted.");
    });

    /// <summary>Deletes every payment and clears the paid flag. Applied account credit stays, as in 1.x.</summary>
    public Notice MarkUnpaid(long invoiceId) => _db.InTransaction((db, tx) =>
    {
        var invoice = Loader.Invoice(db, invoiceId, tx) ?? throw NotFound();
        db.Execute("DELETE FROM payments WHERE invoice_id = @invoiceId", new { invoiceId }, tx);
        invoice.Paid = false;
        invoice.PaidDate = null;
        SaveHeader(db, tx, invoice);
        return Notice.Warning($"Invoice {invoice.InvoiceNumber} marked as unpaid. All payments removed.");
    });

    // ------------------------------------------------------------------ shared

    /// <summary>Field checks from 1.x InvoiceForm. Returns the number as it will be saved.</summary>
    private static string Validate(InvoiceDraft d, long? editingId)
    {
        var checks = new Checks();
        if (d.CustomerId is null or <= 0) checks.Add("customer", Checks.Required);
        checks.RequireText("invoice_number", d.InvoiceNumber, 50);
        if (d.Date is null) checks.Add("date", Checks.Required);
        checks.MaxLength("term1", d.Term1, 300);
        checks.MaxLength("term2", d.Term2, 300);
        checks.ThrowIfAny();
        if (d.Lines.Count == 0) throw new UserFacingException("At least one line item is required.");
        return d.InvoiceNumber.Trim().ToUpperInvariant();
    }

    private static void EnsureUniqueNumber(SqliteConnection db, SqliteTransaction tx, string number, string typed, long? exceptId)
    {
        bool taken = db.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM invoices WHERE invoice_number = @number AND id != @except)",
            new { number, except = exceptId ?? -1 }, tx);
        if (taken)
            throw new ValidationException(
                new Dictionary<string, string> { ["invoice_number"] = "That number is already used." },
                $"Invoice number {typed} already exists.");
    }

    private static void RequireCustomer(SqliteConnection db, SqliteTransaction tx, long customerId, bool mustBeActive)
    {
        bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM customers WHERE id = @customerId", new { customerId }, tx);
        if (active is null || (mustBeActive && active == false))
            throw new ValidationException(new Dictionary<string, string> { ["customer"] = "Not a valid choice." });
    }

    internal static void InsertLines(SqliteConnection db, SqliteTransaction tx, long invoiceId,
        IEnumerable<InvoiceLineDraft> lines, bool skipBlank, bool roundAmounts)
    {
        foreach (var line in lines)
        {
            string description = (line.Description ?? "").Trim();
            if (skipBlank && description.Length == 0) continue;
            if (skipBlank && description.Length > 500) description = description[..500];
            db.Execute("INSERT INTO invoice_lines (invoice_id, description, quantity, amount) VALUES (@invoiceId, @description, @quantity, @amount)",
                new { invoiceId, description, quantity = line.Quantity, amount = roundAmounts ? PyMath.Round(line.Amount, 2) : line.Amount }, tx);
        }
    }

    private static void SaveHeader(SqliteConnection db, SqliteTransaction tx, Invoice i) => db.Execute("""
        UPDATE invoices SET invoice_number = @InvoiceNumber, customer_id = @CustomerId, date = @Date, notes = @Notes,
            term1 = @Term1, term2 = @Term2, paid = @Paid, paid_date = @PaidDate, credit_applied = @CreditApplied
        WHERE id = @Id
        """, new { i.InvoiceNumber, i.CustomerId, i.Date, i.Notes, i.Term1, i.Term2, i.Paid, i.PaidDate, i.CreditApplied, i.Id }, tx);

    private static UserFacingException NotFound() =>
        new("That invoice no longer exists. It may have been deleted.");
}
