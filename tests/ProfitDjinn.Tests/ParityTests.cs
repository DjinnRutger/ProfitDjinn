using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>
/// 2.0 against 1.x, on the same operations and the same data.
///
/// Fixtures/parity_ops.json was run through the real 1.x Flask app by
/// tools/Parity/make_fixture.py, which saved the database it produced (parity.db) and every
/// figure 1.x computed from it (parity_expected.json). Here the same operations go through
/// the 2.0 services, and the two databases must match row for row, value for value.
/// </summary>
public class ParityTests
{
    private static readonly string[] Tables =
        { "customers", "service_items", "invoices", "invoice_lines", "payments", "work_orders", "work_order_lines" };

    /// <summary>
    /// Deliberate differences: 1.x bugs that 2.0 fixes (docs/port-spec.md, "Fixed in 2.0").
    /// Each pins the value 2.0 must have instead, so a fix cannot hide a real regression.
    /// </summary>
    private static readonly Dictionary<(string Table, long Id, string Column), (object Value, string Why)> KnownFixes = new()
    {
        [("invoices", 5, "credit_applied")] = (40.0,
            "1.x's invoice edit capped applied credit against the old lines (its line collection was stale after " +
            "replacing them), so $50 of credit stayed on a $40 invoice and the customer lost $10 of credit."),
    };

    private static readonly DateOnly FixtureToday =DateOnly.Parse(Fixture.Json("parity_expected.json").GetProperty("today").GetString()!);

    [Fact]
    public void Replaying_the_operations_produces_the_same_database_as_1x()
    {
        var store = Fixture.FreshStore(FixtureToday);
        var ops = Fixture.Json("parity_ops.json").GetProperty("ops").EnumerateArray().ToList();
        for (int i = 0; i < ops.Count; i++) Replay(store, ops[i], i);

        // For the rollback check: tools/Parity/dump_figures.py can then open this file with 1.x.
        string? export = Environment.GetEnvironmentVariable("PROFITDJINN_EXPORT_REPLAY");
        if (!string.IsNullOrEmpty(export)) File.Copy(store.Paths.DatabasePath, export, overwrite: true);

        using var mine = Open(store.Paths.DatabasePath);
        using var theirs = Open(Fixture.PathOf("parity.db"));
        foreach (string table in Tables)
        {
            var a = Rows(theirs, table);
            var b = Rows(mine, table);
            Assert.True(a.Count == b.Count, $"{table}: 1.x has {a.Count} rows, 2.0 has {b.Count}");
            for (int r = 0; r < a.Count; r++)
                foreach (var (column, value) in a[r])
                {
                    if (column == "created_at") continue;
                    object? other = b[r].GetValueOrDefault(column);
                    if (KnownFixes.TryGetValue((table, (long)a[r]["id"]!, column), out var fix))
                    {
                        Assert.True(Equal(fix.Value, other), $"{table} {a[r]["id"]} {column}: expected the 2.0 fix {Show(fix.Value)}, got {Show(other)}. {fix.Why}");
                        continue;
                    }
                    Assert.True(Equal(value, other),
                        $"{table} row {r + 1}, column {column}: 1.x {Show(value)}, 2.0 {Show(other)}");
                }
        }
    }

    [Fact]
    public void Figures_computed_from_the_1x_database_match_1x() =>
        AssertFiguresMatch(new Store(Fixture.CopyOf("parity.db"), () => FixtureToday), Fixture.Json("parity_expected.json"));

    /// <summary>
    /// The same check on a copy of a real database, made by tools/Parity/dump_figures.py.
    /// Runs only when PROFITDJINN_PARITY_DIR points at that folder; real data never enters the repo.
    /// </summary>
    [Fact]
    public void RealDatabase_figures_match_1x_when_a_copy_is_provided()
    {
        string? dir = Environment.GetEnvironmentVariable("PROFITDJINN_PARITY_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "real_expected.json"))).RootElement;
        var today = DateOnly.Parse(expected.GetProperty("today").GetString()!);
        var paths = Fixture.TempPaths();
        File.Copy(Path.Combine(dir, "real.db"), paths.DatabasePath);
        AssertFiguresMatch(new Store(paths, () => today), expected);
    }

    private static void AssertFiguresMatch(Store store, JsonElement expected)
    {

        foreach (var inv in store.Invoices.List())
        {
            var e = expected.GetProperty("invoices").GetProperty(inv.Id.ToString(CultureInfo.InvariantCulture));
            string id = $"invoice {inv.InvoiceNumber}";
            Same(e, "total", inv.Total, id);
            Same(e, "net_total", inv.NetTotal, id);
            Same(e, "amount_paid", inv.AmountPaid, id);
            Same(e, "balance_due", inv.BalanceDue, id);
            Same(e, "credit_amount", inv.CreditAmount, id);
            Assert.Equal(e.GetProperty("is_partial").GetBoolean(), inv.IsPartial);
            Assert.Equal(e.GetProperty("status").GetString(), inv.StatusLabel);
            Assert.Equal(e.GetProperty("unit_prices").EnumerateArray().Select(x => x.GetDouble()), inv.Lines.Select(l => l.UnitPrice));
        }

        foreach (var c in store.Customers.List(includeInactive: true))
        {
            var e = expected.GetProperty("customers").GetProperty(c.Id.ToString(CultureInfo.InvariantCulture));
            string id = $"customer {c.Name}";
            Same(e, "total_invoiced", c.TotalInvoiced, id);
            Same(e, "total_outstanding", c.TotalOutstanding, id);
            Same(e, "total_paid", c.TotalPaid, id);
            Same(e, "account_credit", c.AccountCredit, id);
            Assert.Equal(e.GetProperty("full_address").GetString(), c.FullAddress);
        }

        foreach (var wo in store.WorkOrders.List(includeIdle: true))
        {
            var e = expected.GetProperty("work_orders").GetProperty(wo.Id.ToString(CultureInfo.InvariantCulture));
            string id = $"work order {wo.Number}";
            Same(e, "ready_to_bill_total", wo.ReadyToBillTotal, id);
            Same(e, "billed_total", wo.BilledTotal, id);
            Assert.Equal(e.GetProperty("pending_count").GetInt32(), wo.PendingCount);
            Assert.Equal(e.GetProperty("has_open_work").GetBoolean(), wo.HasOpenWork);
            Assert.Equal(e.GetProperty("open_labels").EnumerateArray().Select(x => x.GetString()), wo.OpenLabels);
            Assert.Equal(
                e.GetProperty("grouped_open").EnumerateArray().Select(g => $"{g[0].GetString()}:{string.Join(",", g[1].EnumerateArray().Select(x => x.GetInt64()))}"),
                wo.GroupedOpen().Select(g => $"{g.Label}:{string.Join(",", g.Lines.Select(l => l.Id))}"));
            var billed = wo.GroupedBilled(store.WorkOrders.BilledInvoices(wo));
            Assert.Equal(
                e.GetProperty("grouped_billed").EnumerateArray().Select(g => $"{g[0].GetInt64()}:{g[1].GetDouble():R}:{string.Join(",", g[2].EnumerateArray().Select(x => x.GetInt64()))}"),
                billed.Select(g => $"{g.InvoiceId}:{g.Total:R}:{string.Join(",", g.Lines.Select(l => l.Id))}"));

            foreach (var line in wo.Lines)
            {
                var le = expected.GetProperty("lines").GetProperty(line.Id.ToString(CultureInfo.InvariantCulture));
                Assert.Equal(le.GetProperty("effective_status").GetString(), WorkOrderLine.StatusText(line.EffectiveStatus));
                Assert.Equal(le.GetProperty("status_label").GetString(), line.StatusLabel);
                Assert.Equal(le.GetProperty("quantity_label").GetString(), line.QuantityLabel);
                Assert.Equal(le.GetProperty("type_label").GetString(), line.TypeLabel);
            }
        }

        Assert.Equal(expected.GetProperty("next_invoice_number").GetString(), store.Invoices.NextNumber());

        var dash = store.Reports.Dashboard();
        var d = expected.GetProperty("dashboard");
        Assert.Equal(d.GetProperty("unpaid_invoices").GetInt32(), dash.UnpaidInvoices);
        Same(d, "unpaid_total", dash.UnpaidTotal, "dashboard");
        Same(d, "year_revenue", dash.YearRevenue, "dashboard");
        Assert.Equal(d.GetProperty("active_customers").GetInt32(), dash.ActiveCustomers);
        Assert.Equal(d.GetProperty("total_invoices").GetInt32(), dash.TotalInvoices);

        foreach (var yr in expected.GetProperty("revenue").EnumerateObject())
        {
            var e = yr.Value;
            var report = store.Reports.Revenue(int.Parse(yr.Name, CultureInfo.InvariantCulture));
            string id = $"revenue {yr.Name}";
            Same(e, "total_revenue", report.TotalRevenue, id);
            Same(e, "total_invoiced", report.TotalInvoiced, id);
            Same(e, "total_outstanding", report.TotalOutstanding, id);
            Assert.Equal(e.GetProperty("paid_count").GetInt32(), report.PaidCount);
            Assert.Equal(e.GetProperty("partial_count").GetInt32(), report.PartialCount);
            Assert.Equal(e.GetProperty("monthly_revenue").EnumerateArray().Select(x => x.GetDouble()), report.Months.Select(m => m.Collected));
            Assert.Equal(e.GetProperty("monthly_invoiced").EnumerateArray().Select(x => x.GetDouble()), report.Months.Select(m => m.Invoiced));
            Assert.Equal(
                e.GetProperty("customers").EnumerateArray().Select(c => $"{c[0].GetString()}={c[1].GetDouble():R}"),
                report.Customers.Select(c => $"{c.Name}={c.Collected:R}"));
        }
    }

    /// <summary>The 1.x build must still open a database 2.0 has upgraded: same tables, same columns.</summary>
    [Fact]
    public void Opening_a_1x_database_keeps_every_table_and_column()
    {
        using var before = Open(Fixture.PathOf("parity.db"));
        var paths = Fixture.CopyOf("parity.db");
        _ = new Store(paths, () => FixtureToday);
        using var after = Open(paths.DatabasePath);

        foreach (string table in before.Query<string>("SELECT name FROM sqlite_master WHERE type='table'"))
        {
            var cols = before.Query<string>($"SELECT name FROM pragma_table_info('{table}')").ToList();
            Assert.Equal(cols, after.Query<string>($"SELECT name FROM pragma_table_info('{table}')").ToList());
            Assert.Equal(before.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\""),
                         after.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\"") - (table == "settings" ? 12 : 0));
        }
    }

    // ------------------------------------------------------------------ replay

    private static void Replay(Store s, JsonElement op, int index)
    {
        string kind = op.GetProperty("op").GetString()!;
        bool expectError = op.TryGetProperty("expect_error", out var ee) && ee.GetBoolean();
        try
        {
            Notice? notice = Run(s, op, kind);
            if (expectError && notice?.Kind is not (NoticeKind.Warning or NoticeKind.Danger))
                Assert.Fail($"op {index} ({kind}) should have been refused, as 1.x refused it, but 2.0 accepted it.");
        }
        catch (UserFacingException ex) when (expectError)
        {
            _ = ex;
        }
        catch (UserFacingException ex)
        {
            Assert.Fail($"op {index} ({kind}) was refused by 2.0 but accepted by 1.x: {ex.Message}");
        }
    }

    private static Notice? Run(Store s, JsonElement op, string kind)
    {
        string S(string name) => op.GetProperty(name).GetString()!;
        long L(string name) => op.GetProperty(name).GetInt64();
        bool B(string name) => op.GetProperty(name).GetBoolean();
        DateOnly? D(string name) => DateOnly.TryParseExact(S(name), "yyyy-MM-dd", out var d) ? d : null;

        CustomerDraft Customer() => new(S("name"), S("attn"), S("address"), S("city"), S("state"), S("zip_code"),
            S("phone"), S("email"), S("notes"), B("is_active"));
        InvoiceDraft Invoice() => new(L("customer_id"), S("invoice_number"), D("date"), S("notes"), S("term1"), S("term2"), B("paid"),
            op.GetProperty("lines").EnumerateArray()
              .Select(l => new InvoiceLineDraft(l.GetProperty("description").GetString()!, l.GetProperty("quantity").GetDouble(), l.GetProperty("amount").GetDouble()))
              .ToList());
        LineInput Line() => new(S("description"), S("project_label"), S("line_type"), D("date_performed"),
            S("quantity"), S("rate"), B("no_charge"), S("internal_note"));
        double? Price() => double.TryParse(S("price"), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null;

        switch (kind)
        {
            case "set_setting": s.Settings.Set(S("key"), S("value")); return null;
            case "create_customer": return s.Customers.Create(Customer()).Notice;
            case "edit_customer": return s.Customers.Update(L("id"), Customer());
            case "customer_notes": return s.Customers.UpdateNotes(L("id"), S("notes"));
            case "delete_customer": return s.Customers.Delete(L("id"));
            case "create_item": return s.Items.Create(S("description"), Price(), B("is_active")).Notice;
            case "edit_item": return s.Items.Update(L("id"), S("description"), Price(), B("is_active"));
            case "toggle_item": return s.Items.ToggleActive(L("id"));
            case "delete_item": return s.Items.Delete(L("id"));
            case "create_invoice": return s.Invoices.Create(Invoice()).Notice;
            case "edit_invoice": return s.Invoices.Update(L("id"), Invoice());
            case "delete_invoice": return s.Invoices.Delete(L("id"));
            case "record_payment":
                double amount = double.TryParse(S("amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ? a : 0;
                return s.Invoices.RecordPayment(L("invoice_id"), amount, S("method"), S("check_number"), D("date"), S("notes"));
            case "delete_payment": return s.Invoices.DeletePayment(L("invoice_id"), L("payment_id"));
            case "mark_unpaid": return s.Invoices.MarkUnpaid(L("invoice_id"));
            case "open_tab": s.WorkOrders.ForCustomer(L("customer_id")); return null;
            case "add_todo": return s.WorkOrders.AddTodo(L("wo_id"), S("description"), S("project_label"));
            case "log_work": return s.WorkOrders.LogWork(L("wo_id"), Line());
            case "edit_line": return s.WorkOrders.EditLine(L("line_id"), Line());
            case "complete_line": return s.WorkOrders.CompleteLine(L("line_id"), Line());
            case "reopen_line": return s.WorkOrders.ReopenLine(L("line_id"));
            case "toggle_no_charge": return s.WorkOrders.ToggleNoCharge(L("line_id"));
            case "delete_line": return s.WorkOrders.DeleteLine(L("line_id"));
            case "wo_notes": return s.WorkOrders.UpdateNotes(L("wo_id"), S("notes"));
            case "bill":
                var rows = op.GetProperty("rows").EnumerateArray()
                    .Select(r => new InvoiceRowInput(r.GetProperty("description").GetString()!, r.GetProperty("quantity").GetString()!, r.GetProperty("unit_price").GetString()!))
                    .ToList();
                var ids = op.GetProperty("selected_line_ids").EnumerateArray().Select(x => x.GetInt64()).ToList();
                return s.Billing.Submit(L("wo_id"), new BillHeader(S("invoice_number"), D("date"), S("notes"), S("term1"), S("term2")), ids, rows).Notice;
            default: throw new InvalidOperationException($"Unknown op {kind}");
        }
    }

    // ------------------------------------------------------------------ comparing

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        c.Open();
        return c;
    }

    private static List<Dictionary<string, object?>> Rows(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {table} ORDER BY id";
        using var r = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static bool Equal(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        (long x, double y) => x == y,
        (double x, long y) => x == y,
        _ => Equals(a, b),
    };

    private static string Show(object? v) => v switch
    {
        null => "NULL",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        string str => $"'{str}'",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)!,
    };

    private static void Same(JsonElement e, string key, double actual, string what)
    {
        double expected = e.GetProperty(key).GetDouble();
        Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            $"{what} {key}: 1.x {expected:R}, 2.0 {actual:R}");
    }
}
