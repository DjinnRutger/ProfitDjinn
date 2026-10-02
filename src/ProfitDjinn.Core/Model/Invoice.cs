using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

public enum InvoiceStatus { Paid, Partial, Unpaid }

/// <summary>
/// An invoice with its lines and payments. Every money property here is a straight port of
/// the 1.x properties in app/models/invoice.py, including their order of operations, so the
/// same database shows the same figures in both builds.
/// </summary>
public sealed class Invoice
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public long CustomerId { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; } = "";
    public string? Term1 { get; set; } = "";
    public string? Term2 { get; set; } = "";
    public bool Paid { get; set; }
    public DateOnly? PaidDate { get; set; }
    public double CreditApplied { get; set; }
    public string? CreatedAt { get; set; }

    public List<InvoiceLine> Lines { get; set; } = new();

    /// <summary>Ordered by payment date, as 1.x ordered them.</summary>
    public List<Payment> Payments { get; set; } = new();

    /// <summary>Filled in by services that show the customer next to the invoice.</summary>
    public Customer? Customer { get; set; }

    /// <summary>Sum of line amounts. A line's amount is its extended total, not a unit price.</summary>
    public double Total => PyMath.Sum(Lines, l => l.Amount);

    /// <summary>What the customer owes after account credit applied to this invoice.</summary>
    public double NetTotal => Math.Max(0.0, Total - CreditApplied);

    /// <summary>
    /// Sum of payments. An invoice marked paid with no payment records counts as fully paid:
    /// that is how invoices from before payment tracking behave.
    /// </summary>
    public double AmountPaid
    {
        get
        {
            if (Payments.Count > 0) return PyMath.Sum(Payments, p => p.Amount);
            return Paid ? NetTotal : 0.0;
        }
    }

    public double BalanceDue => Math.Max(0.0, NetTotal - AmountPaid);

    /// <summary>Overpayment, which becomes account credit for the customer.</summary>
    public double CreditAmount => Math.Max(0.0, AmountPaid - NetTotal);

    public bool IsPartial => AmountPaid > 0 && AmountPaid < NetTotal;

    public InvoiceStatus Status => Paid ? InvoiceStatus.Paid : IsPartial ? InvoiceStatus.Partial : InvoiceStatus.Unpaid;

    public string StatusLabel => Status.ToString();

    /// <summary>
    /// 1.x <c>_recalc_paid_status</c>: paid once the payments and credit cover the net total.
    /// The first paid date is kept.
    /// </summary>
    public void RecalcPaidStatus(DateOnly when)
    {
        if (AmountPaid >= NetTotal)
        {
            Paid = true;
            PaidDate ??= when;
        }
        else
        {
            Paid = false;
            PaidDate = null;
        }
    }

    /// <summary>Distinct payment method labels, in payment order ("Paid Via").</summary>
    public string PaidVia => string.Join(", ", Payments.Select(p => p.MethodLabel).Distinct());
}

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public double Quantity { get; set; } = 1.0;

    /// <summary>The line's extended total (quantity times unit price).</summary>
    public double Amount { get; set; }

    public double UnitPrice => Quantity != 0 ? Amount / Quantity : Amount;
}
