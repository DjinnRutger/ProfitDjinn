namespace ProfitDjinn.Core.Model;

/// <summary>A reusable price-list entry. Quick-adds a line to an invoice or a work order.</summary>
public sealed class ServiceItem
{
    public long Id { get; set; }
    public string Description { get; set; } = "";
    public double Price { get; set; }
    public bool IsActive { get; set; } = true;
}
