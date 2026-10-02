using System.Text.Json;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Tests;

/// <summary>
/// The bill screen's roll-ups against what 1.x's own JavaScript produced for the same work
/// (Fixtures/rollup_expected.json, written by tools/Parity/rollup_reference.mjs).
/// </summary>
public class RollupTests
{
    public static IEnumerable<object[]> Cases()
    {
        var cases = Fixture.Json("rollup_cases.json").GetProperty("cases").EnumerateArray().ToList();
        for (int i = 0; i < cases.Count; i++)
            foreach (string mode in new[] { "one", "type", "detailed" })
                yield return new object[] { i, mode, cases[i].GetProperty("name").GetString()! };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_1x_javascript(int index, string mode, string name)
    {
        var input = Fixture.Json("rollup_cases.json").GetProperty("cases")[index];
        var expected = Fixture.Json("rollup_expected.json").GetProperty("cases")[index];
        Assert.Equal(name, expected.GetProperty("name").GetString());

        var lines = input.GetProperty("lines").EnumerateArray().Select(ToLine).ToList();
        var rows = Rollup.Build(ParseMode(mode), lines);
        var want = expected.GetProperty("modes").GetProperty(mode);

        var wantRows = want.GetProperty("rows").EnumerateArray()
            .Select(r => new InvoiceRowInput(r.GetProperty("description").GetString()!, r.GetProperty("quantity").GetString()!, r.GetProperty("unit_price").GetString()!))
            .ToList();
        Assert.Equal(wantRows, rows);

        Assert.Equal(expected.GetProperty("selected_total").GetDouble(), Rollup.SelectedTotal(lines));
        Assert.Equal(want.GetProperty("total").GetDouble(), InvoiceRows.Total(rows));
        var mismatch = want.GetProperty("mismatch");
        Assert.Equal(mismatch.ValueKind == JsonValueKind.Null ? null : mismatch.GetDouble(), Rollup.Mismatch(rows, lines));
    }

    [Fact]
    public void Billable_lines_sort_projects_then_general_then_date()
    {
        var lines = new[]
        {
            new WorkOrderLine { Id = 1, ProjectLabel = "", DatePerformed = new DateOnly(2026, 1, 1) },
            new WorkOrderLine { Id = 2, ProjectLabel = "porch", DatePerformed = new DateOnly(2026, 1, 5) },
            new WorkOrderLine { Id = 3, ProjectLabel = "Kitchen", DatePerformed = null },
            new WorkOrderLine { Id = 4, ProjectLabel = "Porch", DatePerformed = new DateOnly(2026, 1, 2) },
            new WorkOrderLine { Id = 5, ProjectLabel = "Kitchen", DatePerformed = new DateOnly(2026, 1, 1) },
        };
        Assert.Equal(new long[] { 3, 5, 4, 2, 1 }, Rollup.SortBillable(lines).Select(l => l.Id));
    }

    private static RollupMode ParseMode(string m) => m switch
    {
        "type" => RollupMode.Type,
        "detailed" => RollupMode.Detailed,
        _ => RollupMode.One,
    };

    private static WorkOrderLine ToLine(JsonElement l)
    {
        string iso = l.GetProperty("iso").GetString()!;
        string label = l.GetProperty("label").GetString()!;
        return new WorkOrderLine
        {
            Id = l.GetProperty("id").GetInt64(),
            ProjectLabel = label == WorkOrder.GeneralLabel ? "" : label,
            LineType = l.GetProperty("type").GetString()!,
            Description = l.GetProperty("description").GetString()!,
            DatePerformed = iso.Length == 0 ? null : DateOnly.Parse(iso),
            Quantity = l.GetProperty("qty").GetDouble(),
            Rate = l.GetProperty("rate").GetDouble(),
            Amount = l.GetProperty("amount").GetDouble(),
            NoCharge = l.GetProperty("noCharge").GetBoolean(),
            Status = "completed",
        };
    }
}
