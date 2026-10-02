using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

public sealed class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Attn { get; set; } = "";
    public string? Address { get; set; } = "";
    public string? City { get; set; } = "";
    public string? State { get; set; } = "";
    public string? ZipCode { get; set; } = "";
    public string? Phone { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? CreatedAt { get; set; }

    /// <summary>Loaded by the services when a screen needs the derived totals.</summary>
    public List<Invoice> Invoices { get; set; } = new();

    // ---- derived totals (1.x models/customer.py) ----

    public double TotalInvoiced => PyMath.Sum(Invoices, i => i.Total);

    public double TotalOutstanding => PyMath.Sum(Invoices, i => i.BalanceDue);

    /// <summary>Invoiced minus outstanding, so applied credit counts as paid and overpayment does not.</summary>
    public double TotalPaid => TotalInvoiced - TotalOutstanding;

    /// <summary>Overpayments not yet used on another invoice.</summary>
    public double AccountCredit =>
        Math.Max(0.0, PyMath.Sum(Invoices, i => i.CreditAmount) - PyMath.Sum(Invoices, i => i.CreditApplied));

    /// <summary>"Street, City, ST, ZIP" with the empty parts left out.</summary>
    public string FullAddress =>
        string.Join(", ", new[] { Address, City, State, ZipCode }.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>"City, ST" for the customer list, or empty.</summary>
    public string CityState =>
        string.Join(", ", new[] { City, State }.Where(p => !string.IsNullOrEmpty(p)));
}
