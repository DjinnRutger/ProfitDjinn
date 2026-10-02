using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>Everything the bill screen needs when it opens.</summary>
public sealed record BillSetup(
    WorkOrder WorkOrder,
    IReadOnlyList<WorkOrderLine> Lines,
    IReadOnlySet<long> PreselectedIds,
    string InvoiceNumber,
    DateOnly Date,
    string Term1,
    string Term2,
    string? LabelFilter);

/// <summary>The bill screen's header fields.</summary>
public sealed record BillHeader(string InvoiceNumber, DateOnly? Date, string Notes, string Term1, string Term2);

/// <summary>Turns completed work on a tab into an invoice. A port of 1.x bill() and bill_submit().</summary>
public sealed class BillingService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly InvoiceService _invoices;
    private readonly Func<DateOnly> _today;

    public BillingService(Database db, SettingsService settings, InvoiceService invoices, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _invoices = invoices;
        _today = today;
    }

    /// <summary>
    /// Opens the bill screen. <paramref name="label"/> limits it to one project;
    /// <paramref name="onlyIds"/> preselects specific lines instead of everything billable.
    /// Throws a UserFacingException when there is nothing to bill.
    /// </summary>
    public BillSetup Prepare(long workOrderId, string? label = null, IReadOnlyCollection<long>? onlyIds = null)
    {
        var wo = _db.Run(db => Loader.WorkOrder(db, workOrderId)) ?? throw new UserFacingException("That work order no longer exists.");
        var completed = wo.CompletedLines.ToList();
        if (completed.Count == 0) throw new UserFacingException("There is no completed work on this tab to bill yet.");

        string filter = (label ?? "").Trim();
        var billable = completed;
        if (filter.Length > 0)
        {
            billable = completed.Where(l => l.LabelOrGeneral == filter).ToList();
            if (billable.Count == 0) throw new UserFacingException($"No completed work found under '{filter}'.");
        }

        // 1.x preselects from the whole tab, not just the filtered project.
        var preselected = onlyIds is null or { Count: 0 }
            ? completed.Where(l => !l.NoCharge).Select(l => l.Id).ToHashSet()
            : completed.Where(l => onlyIds.Contains(l.Id)).Select(l => l.Id).ToHashSet();

        return new BillSetup(wo, Rollup.SortBillable(billable), preselected, _invoices.NextNumber(), _today(),
            _settings.Get(SettingKeys.InvoiceTerm1, "Payment Terms: Due within 30 days"),
            _settings.Get(SettingKeys.InvoiceTerm2, "Make all checks payable to Your Name"),
            filter.Length > 0 ? filter : null);
    }

    /// <summary>
    /// Creates the invoice and marks the selected work billed. Every selected line must still be
    /// completed work on this tab: that is what stops the same work being billed twice.
    /// </summary>
    public Created Submit(long workOrderId, BillHeader header, IReadOnlyCollection<long> selectedIds, IReadOnlyList<InvoiceRowInput> rows)
    {
        var checks = new Checks();
        checks.RequireText("invoice_number", header.InvoiceNumber, 50);
        if (header.Date is null) checks.Add("date", Checks.Required);
        checks.MaxLength("term1", header.Term1, 300);
        checks.MaxLength("term2", header.Term2, 300);
        checks.ThrowIfAny("Please correct the highlighted fields.");

        return _db.InTransaction((db, tx) =>
        {
            var wo = Loader.WorkOrder(db, workOrderId, tx) ?? throw new UserFacingException("That work order no longer exists.");
            var wanted = selectedIds.ToHashSet();
            var valid = wo.Lines.Where(l => wanted.Contains(l.Id) && l.EffectiveStatus == LineStatus.Completed).ToList();
            if (valid.Count == 0) throw new UserFacingException("Select at least one completed line to bill.");
            if (valid.Count != wanted.Count)
                throw new SelectionChangedException(
                    "Some of the selected work was billed or changed since this page was opened. Check the selection and try again.");
            if (rows.Count == 0) throw new UserFacingException("The invoice needs at least one line item.");

            string number = header.InvoiceNumber.Trim().ToUpperInvariant();
            if (db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM invoices WHERE invoice_number = @number)", new { number }, tx))
                throw new ValidationException(
                    new Dictionary<string, string> { ["invoice_number"] = "That number is already used." },
                    $"Invoice number {number} already exists.");

            long invoiceId = db.ExecuteScalar<long>("""
                INSERT INTO invoices (invoice_number, customer_id, date, notes, term1, term2, paid, paid_date, credit_applied, created_at)
                VALUES (@number, @customer, @date, @notes, @term1, @term2, 0, NULL, 0, @now);
                SELECT last_insert_rowid();
                """, new
            {
                number,
                customer = wo.CustomerId,
                date = header.Date!.Value,
                notes = header.Notes ?? "",
                term1 = header.Term1 ?? "",
                term2 = header.Term2 ?? "",
                now = SqlFormat.NowUtc(),
            }, tx);

            // Rows with no description are dropped; amounts are rounded to cents (1.x bill_submit).
            InvoiceService.InsertLines(db, tx, invoiceId, rows.Select(InvoiceRows.ToDraft), skipBlank: true, roundAmounts: true);

            DateOnly billedOn = _today();
            foreach (var line in valid)
                db.Execute("UPDATE work_order_lines SET status = 'billed', invoice_id = @invoiceId, billed_at = @billedOn WHERE id = @Id",
                    new { invoiceId, billedOn, line.Id }, tx);

            return new Created(invoiceId, Notice.Success(
                $"Invoice {number} created from {valid.Count} work order {Fmt.Plural(valid.Count, "line")}."));
        });
    }
}

/// <summary>The selection is stale. The screen should reload the bill setup, as 1.x redirected.</summary>
public sealed class SelectionChangedException : UserFacingException
{
    public SelectionChangedException(string message) : base(message) { }
}
