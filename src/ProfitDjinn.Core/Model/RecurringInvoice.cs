using System.Globalization;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

/// <summary>2.4. How often a recurring invoice repeats. Stripe's Price.recurring.interval values.</summary>
public static class BillingInterval
{
    public const string Month = "month";
    public const string Year = "year";
    public static bool IsValid(string? i) => i is Month or Year;
}

/// <summary>
/// 2.4. Who delivers the invoice and collects the money. Only <see cref="SendInvoice"/> exists
/// today: ProfitDjinn creates the invoice and the user sends it. The values are Stripe's
/// collection_method, so a later Stripe option ("charge_automatically") fits the same column.
/// </summary>
public static class CollectionMethod
{
    public const string SendInvoice = "send_invoice";
}

/// <summary>2.4. A schedule that creates the same invoice for a customer every month or year.</summary>
public sealed class RecurringInvoice
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Interval { get; set; } = BillingInterval.Month;
    public int IntervalCount { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; } = "";
    public string? Term1 { get; set; } = "";
    public string? Term2 { get; set; } = "";
    public string CollectionMethod { get; set; } = Model.CollectionMethod.SendInvoice;
    public bool IsActive { get; set; } = true;

    /// <summary>The last date invoices were created up to. Nothing on or before it is ever created again.</summary>
    public DateOnly? GeneratedThrough { get; set; }
    public string? CreatedAt { get; set; }

    public List<RecurringInvoiceLine> Lines { get; set; } = new();
    public Customer? Customer { get; set; }

    /// <summary>Each invoice's total. Placeholders do not change amounts.</summary>
    public double Total => PyMath.Sum(Lines, l => l.Amount);

    public Schedule Schedule => new(StartDate, (Interval == BillingInterval.Year ? 12 : 1) * Math.Max(1, IntervalCount), DayOfMonth, EndDate);

    /// <summary>The first date not yet invoiced, or null when the schedule has ended.</summary>
    public DateOnly? NextDate => Schedule.FirstFrom(GeneratedThrough is { } g ? g.AddDays(1) : StartDate);

    public string ScheduleLabel
    {
        get
        {
            bool yearly = Interval == BillingInterval.Year;
            string when = yearly
                ? $"on {new DateOnly(2000, StartDate.Month, 1).ToString("MMM", CultureInfo.InvariantCulture)} {DayOfMonth}"
                : $"on day {DayOfMonth}";
            if (IntervalCount <= 1) return $"{(yearly ? "Yearly" : "Monthly")} {when}";
            return $"Every {IntervalCount} {(yearly ? "years" : "months")} {when}";
        }
    }
}

public sealed class RecurringInvoiceLine
{
    public long Id { get; set; }
    public long RecurringId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public double Quantity { get; set; } = 1.0;

    /// <summary>The line's extended total, as on invoice_lines.</summary>
    public double Amount { get; set; }

    public double UnitPrice => Quantity != 0 ? Amount / Quantity : Amount;
}
