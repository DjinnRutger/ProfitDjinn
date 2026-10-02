using System.Globalization;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Rules;

public enum RollupMode { One, Type, Detailed }

/// <summary>
/// Builds the invoice rows on the bill screen from the selected work. A straight port of
/// buildRollup() and addRow() in 1.x's work_orders/bill.html, including its rounding: the
/// unit price is shown (and so saved) rounded to 2 places with toFixed, while the "Selected"
/// total uses the exact line amounts. Three hours totalling $100 therefore bill as 3 x 33.33 =
/// 99.99, and the screen shows the mismatch warning. That is deliberate 1.x behaviour.
/// </summary>
public static class Rollup
{
    public static readonly IReadOnlyDictionary<RollupMode, string> Hints = new Dictionary<RollupMode, string>
    {
        [RollupMode.One] = "One line on the invoice. Cleanest for the customer.",
        [RollupMode.Type] = "One line per Labor / Parts / Services. Labor shows total hours.",
        [RollupMode.Detailed] = "Every selected line appears on the invoice, dated.",
    };

    private static readonly IReadOnlyDictionary<string, string> TypeDescriptions = new Dictionary<string, string>
    {
        [LineTypes.Labor] = "Labor",
        [LineTypes.Part] = "Parts & materials",
        [LineTypes.Service] = "Services",
        [LineTypes.Other] = "Other work",
    };

    /// <summary>
    /// The rows as the bill screen first shows them: the quantity as JavaScript prints it and
    /// the unit price as toFixed(2) text. <paramref name="selected"/> must be in screen order
    /// (<see cref="SortBillable"/>).
    /// </summary>
    public static IReadOnlyList<InvoiceRowInput> Build(RollupMode mode, IReadOnlyList<WorkOrderLine> selected)
    {
        if (selected.Count == 0) return Array.Empty<InvoiceRowInput>();
        var billable = selected.Where(l => !l.NoCharge).ToList();
        var rows = new List<(string Desc, double Qty, double Unit)>();

        switch (mode)
        {
            case RollupMode.Detailed:
                foreach (var l in selected)
                {
                    string d = (l.DatePerformed is { } dt ? ShortDate(dt) + " - " : "") + l.Description;
                    if (l.NoCharge) d += " (no charge)";
                    // 1.x used qty || 1 here, billing a 0-hour line as one hour. Fixed in 2.0.
                    rows.Add((d, l.Quantity, l.NoCharge ? 0 : l.Rate));
                }
                break;

            case RollupMode.Type:
                foreach (string t in LineTypes.All)
                {
                    var group = billable.Where(l => l.LineType == t).ToList();
                    if (group.Count == 0) continue;
                    double amt = PyMath.JsSum(group.Select(l => l.Amount));
                    double qty = t == LineTypes.Labor ? PyMath.JsSum(group.Select(l => l.Quantity)) : 1;
                    if (qty == 0) qty = 1;
                    rows.Add((TypeDescriptions[t] + DateRange(group), JsMathRound(qty * 100) / 100, amt / qty));
                }
                break;

            default:
                double total = PyMath.JsSum(billable.Select(l => l.Amount));
                var labels = selected.Select(l => l.LabelOrGeneral).Where(s => s.Length > 0).Distinct().ToList();
                string baseText = labels.Count == 1 ? labels[0] : "Service work";
                rows.Add((baseText + DateRange(selected), 1, total));
                break;
        }

        return rows.Select(r => new InvoiceRowInput(r.Desc, InvoiceRows.JsNumberText(r.Qty), PyMath.JsToFixedText(r.Unit, 2))).ToList();
    }

    /// <summary>"Selected" on the bill screen: the exact amounts of the selected billable lines.</summary>
    public static double SelectedTotal(IEnumerable<WorkOrderLine> selected) =>
        PyMath.JsSum(selected.Select(l => l.NoCharge ? 0 : l.Amount));

    /// <summary>
    /// The difference shown in the mismatch warning, or null when there is none to show:
    /// rows exist and the invoice total differs from the selected total by a cent or more.
    /// </summary>
    public static double? Mismatch(IReadOnlyList<InvoiceRowInput> rows, IEnumerable<WorkOrderLine> selected)
    {
        if (rows.Count == 0) return null;
        double diff = InvoiceRows.Total(rows) - SelectedTotal(selected);
        return Math.Abs(diff) >= 0.01 ? diff : null;
    }

    /// <summary>
    /// The order the bill screen lists completed work in (1.x <c>_sorted_billable</c>):
    /// labelled projects A-Z (case-insensitive), General last, then date (undated first), then id.
    /// </summary>
    public static List<WorkOrderLine> SortBillable(IEnumerable<WorkOrderLine> lines) =>
        lines.OrderBy(l => l.LabelOrGeneral == WorkOrder.GeneralLabel)
             .ThenBy(l => l.LabelOrGeneral.ToLowerInvariant(), StringComparer.Ordinal)
             .ThenBy(l => l.DatePerformed ?? DateOnly.MinValue)
             .ThenBy(l => l.Id)
             .ToList();

    /// <summary>" (Jul 04)" or " (Jul 04 - Aug 12)" across the dated lines; empty when none are dated.</summary>
    public static string DateRange(IEnumerable<WorkOrderLine> lines)
    {
        var dated = lines.Where(l => l.DatePerformed is not null).OrderBy(l => l.DatePerformed!.Value).ToList();
        if (dated.Count == 0) return "";
        string first = ShortDate(dated[0].DatePerformed!.Value);
        string last = ShortDate(dated[^1].DatePerformed!.Value);
        return first == last ? $" ({first})" : $" ({first} - {last})";
    }

    /// <summary>Python strftime('%b %d'): "Jul 04".</summary>
    public static string ShortDate(DateOnly d) => d.ToString("MMM dd", CultureInfo.InvariantCulture);

    /// <summary>JavaScript Math.round: nearest integer, exact halves toward +infinity.</summary>
    internal static double JsMathRound(double x)
    {
        double f = Math.Floor(x);
        return x - f >= 0.5 ? f + 1 : f;
    }
}
