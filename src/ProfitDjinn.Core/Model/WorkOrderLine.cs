using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

public enum LineStatus { Pending, Completed, Billed }

public static class LineTypes
{
    public const string Labor = "labor";
    public const string Part = "part";
    public const string Service = "service";
    public const string Other = "other";

    /// <summary>In the order the dialog and the "by type" roll-up use them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Labor, Part, Service, Other };

    public static string Label(string? type) => type switch
    {
        Labor => "Labor",
        Part => "Parts",
        Service => "Services",
        _ => "Other",
    };

    public static bool IsValid(string? type) => type is Labor or Part or Service or Other;
}

/// <summary>One piece of work on a customer's tab: a to-do, finished work, or billed work.</summary>
public sealed class WorkOrderLine
{
    public long Id { get; set; }
    public long WorkOrderId { get; set; }
    public string? ProjectLabel { get; set; } = "";
    public string Description { get; set; } = "";
    public string LineType { get; set; } = LineTypes.Labor;

    /// <summary>Stored text: "pending", "completed" or "billed".</summary>
    public string Status { get; set; } = "pending";

    public DateOnly? DatePerformed { get; set; }
    public double Quantity { get; set; } = 1.0;
    public double Rate { get; set; }
    public double Amount { get; set; }
    public bool NoCharge { get; set; }
    public string? InternalNote { get; set; } = "";
    public long? InvoiceId { get; set; }
    public DateOnly? BilledAt { get; set; }
    public string? CreatedAt { get; set; }

    public LineStatus StoredStatus => Status switch
    {
        "completed" => LineStatus.Completed,
        "billed" => LineStatus.Billed,
        _ => LineStatus.Pending,
    };

    /// <summary>
    /// A billed line whose invoice is gone counts as completed again, so the work is never
    /// lost (1.x <c>effective_status</c>). Invoice delete resets lines properly; this is the
    /// second safety net for any other way an invoice disappears.
    /// </summary>
    public LineStatus EffectiveStatus =>
        StoredStatus == LineStatus.Billed && InvoiceId is null ? LineStatus.Completed : StoredStatus;

    public bool IsBilled => EffectiveStatus == LineStatus.Billed;

    public string LabelOrGeneral => string.IsNullOrWhiteSpace(ProjectLabel) ? WorkOrder.GeneralLabel : ProjectLabel.Trim();

    public string TypeLabel => LineTypes.Label(LineType);

    public string StatusLabel => EffectiveStatus switch
    {
        LineStatus.Billed => "Billed",
        LineStatus.Completed => NoCharge ? "No Charge" : "Ready to Bill",
        _ => "Pending",
    };

    /// <summary>"2.5 h" for labor, "3" otherwise.</summary>
    public string QuantityLabel => LineType == LineTypes.Labor ? $"{PyMath.G(Quantity)} h" : PyMath.G(Quantity);

    /// <summary>amount = round(quantity * rate, 2). Called after any field change.</summary>
    public void RecalcAmount() => Amount = PyMath.Round(Quantity * Rate, 2);

    public static string StatusText(LineStatus s) => s switch
    {
        LineStatus.Completed => "completed",
        LineStatus.Billed => "billed",
        _ => "pending",
    };
}
