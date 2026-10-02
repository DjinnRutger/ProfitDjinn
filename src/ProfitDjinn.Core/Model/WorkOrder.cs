using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

/// <summary>
/// One rolling tab per customer. Work is logged as it happens and billed later, so nothing
/// billable is forgotten. Port of 1.x app/models/work_order.py.
/// </summary>
public sealed class WorkOrder
{
    public const string GeneralLabel = "General";

    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Number { get; set; } = "";
    public string? Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? CreatedAt { get; set; }

    /// <summary>Ordered by id, as 1.x ordered them.</summary>
    public List<WorkOrderLine> Lines { get; set; } = new();

    public Customer? Customer { get; set; }

    public IEnumerable<WorkOrderLine> PendingLines => Lines.Where(l => l.EffectiveStatus == LineStatus.Pending);

    /// <summary>Includes no-charge lines.</summary>
    public IEnumerable<WorkOrderLine> CompletedLines => Lines.Where(l => l.EffectiveStatus == LineStatus.Completed);

    public IEnumerable<WorkOrderLine> BilledLines => Lines.Where(l => l.EffectiveStatus == LineStatus.Billed);

    public int PendingCount => PendingLines.Count();

    public double ReadyToBillTotal => PyMath.Sum(CompletedLines.Where(l => !l.NoCharge), l => l.Amount);

    public double BilledTotal => PyMath.Sum(BilledLines.Where(l => !l.NoCharge), l => l.Amount);

    public bool HasOpenWork => Lines.Any(l => l.EffectiveStatus != LineStatus.Billed);

    /// <summary>
    /// Pending and completed lines by project label, labelled groups in ordinal order with
    /// "General" last (1.x <c>grouped_open</c>; Python's sorted() is ordinal, case-sensitive).
    /// </summary>
    public IReadOnlyList<LineGroup> GroupedOpen()
    {
        var groups = new Dictionary<string, List<WorkOrderLine>>();
        foreach (var line in Lines.Where(l => l.EffectiveStatus != LineStatus.Billed))
        {
            if (!groups.TryGetValue(line.LabelOrGeneral, out var list)) groups[line.LabelOrGeneral] = list = new();
            list.Add(line);
        }
        var ordered = groups.Keys.Where(k => k != GeneralLabel).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (groups.ContainsKey(GeneralLabel)) ordered.Add(GeneralLabel);
        return ordered.Select(k => new LineGroup(k, groups[k])).ToList();
    }

    /// <summary>
    /// Billed lines by invoice, newest first (1.x <c>grouped_billed</c>). A group's total
    /// includes no-charge lines. <paramref name="invoices"/> maps invoice id to invoice;
    /// a missing entry means the invoice was deleted.
    /// </summary>
    public IReadOnlyList<BilledGroup> GroupedBilled(IReadOnlyDictionary<long, Invoice> invoices)
    {
        return Lines.Where(l => l.EffectiveStatus == LineStatus.Billed)
            .GroupBy(l => l.InvoiceId)
            .Select(g =>
            {
                var lines = g.ToList();
                invoices.TryGetValue(g.Key ?? 0, out var invoice);
                return new BilledGroup(invoice, g.Key, lines.First().BilledAt, lines, PyMath.Sum(lines, l => l.Amount));
            })
            .OrderByDescending(b => b.BilledAt ?? DateOnly.MinValue)
            .ThenByDescending(b => b.InvoiceId ?? 0)
            .ToList();
    }

    /// <summary>Every distinct project label used on this tab, for the Project suggestions.</summary>
    public IReadOnlyList<string> OpenLabels =>
        Lines.Select(l => (l.ProjectLabel ?? "").Trim()).Where(s => s.Length > 0).Distinct()
             .OrderBy(s => s, StringComparer.Ordinal).ToList();
}

public sealed record LineGroup(string Label, IReadOnlyList<WorkOrderLine> Lines)
{
    public int PendingCount => Lines.Count(l => l.EffectiveStatus == LineStatus.Pending);

    /// <summary>
    /// Ready-to-bill amount for the group. 1.x counted only lines whose stored status is
    /// completed; 2.0 uses the effective status, so an orphaned billed line counts too.
    /// </summary>
    public double ReadyTotal => PyMath.Sum(Lines.Where(l => l.EffectiveStatus == LineStatus.Completed && !l.NoCharge), l => l.Amount);
}

public sealed record BilledGroup(Invoice? Invoice, long? InvoiceId, DateOnly? BilledAt, IReadOnlyList<WorkOrderLine> Lines, double Total);
