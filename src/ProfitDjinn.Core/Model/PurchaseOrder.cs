using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

/// <summary>2.9. Where a purchase order stands. Set by hand; nothing else reads it yet.</summary>
public static class PurchaseOrderStatus
{
    public const string Open = "open";
    public const string Received = "received";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? s) => s is Open or Received or Cancelled;

    public static string Label(string? s) => s switch
    {
        Received => "Received",
        Cancelled => "Cancelled",
        _ => "Open",
    };
}

/// <summary>
/// 2.9. An order to a vendor. Lines keep quantity and unit price apart so that a later inventory
/// feature can take a received line into stock at its cost; today a PO is only a document.
/// </summary>
public sealed class PurchaseOrder
{
    public long Id { get; set; }
    public string PoNumber { get; set; } = "";
    public long VendorId { get; set; }
    public DateOnly Date { get; set; }
    public string Status { get; set; } = PurchaseOrderStatus.Open;
    public DateOnly? ReceivedDate { get; set; }
    public string? Notes { get; set; } = "";
    public string? CreatedAt { get; set; }

    public Vendor? Vendor { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = new();

    public double Total => PyMath.Sum(Lines, l => l.Amount);

    public string StatusLabel => PurchaseOrderStatus.Label(Status);

    public string VendorName => Vendor?.Name ?? "";
}

public sealed class PurchaseOrderLine
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }

    /// <summary>Quantity times unit price, stored as computed when saved (as invoice lines are).</summary>
    public double Amount { get; set; }
}
