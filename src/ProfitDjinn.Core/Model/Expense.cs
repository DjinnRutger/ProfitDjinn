using System.Globalization;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Model;

/// <summary>
/// 2.2. Something the business bought or owes. There is no paid flag: the status comes from
/// the payments alone, so a report can count an expense by its own date (accrual) or by its
/// payment dates (cash).
/// </summary>
public sealed class Expense
{
    public long Id { get; set; }
    public long? VendorId { get; set; }
    public long CategoryId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Description { get; set; } = "";
    public string? Reference { get; set; } = "";
    public double Amount { get; set; }
    public string? Notes { get; set; } = "";
    public long? RecurringId { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Ordered by payment date.</summary>
    public List<ExpensePayment> Payments { get; set; } = new();
    public List<ExpenseReceipt> Receipts { get; set; } = new();
    public Vendor? Vendor { get; set; }
    public ExpenseCategory? Category { get; set; }

    public double AmountPaid => PyMath.Sum(Payments, p => p.Amount);

    public double BalanceDue => Math.Max(0.0, PyMath.Round(Amount - AmountPaid, 2));

    public InvoiceStatus Status =>
        BalanceDue <= 0 ? InvoiceStatus.Paid : AmountPaid > 0 ? InvoiceStatus.Partial : InvoiceStatus.Unpaid;

    public string StatusLabel => Status.ToString();

    /// <summary>Money still owed and the due date has passed.</summary>
    public bool IsOverdue(DateOnly today) => BalanceDue > 0 && DueDate is { } due && due < today;

    public string VendorName => Vendor?.Name ?? "";

    public string CategoryName => Category?.Name ?? "";

    /// <summary>Distinct payment method labels, in payment order ("Paid Via").</summary>
    public string PaidVia => string.Join(", ", Payments.Select(p => p.MethodLabel).Distinct());
}

public sealed class ExpensePayment
{
    public long Id { get; set; }
    public long ExpenseId { get; set; }
    public double Amount { get; set; }
    public string Method { get; set; } = "cash";
    public string? CheckNumber { get; set; } = "";
    public DateOnly Date { get; set; }
    public string? Notes { get; set; } = "";
    public string? CreatedAt { get; set; }

    /// <summary>2.6. Who funded it: the business, the owner's own money (to be paid back), or no cash at all.</summary>
    public string PaidFrom { get; set; } = Model.PaidFrom.Business;

    /// <summary>2.6. The bank account it was paid from, when Bank Accounts is used.</summary>
    public long? AccountId { get; set; }

    /// <summary>2.6. When an owner-paid amount was paid back to the owner.</summary>
    public DateOnly? ReimbursedOn { get; set; }

    public string MethodLabel => PaidFrom == Model.PaidFrom.NoCash ? "No cash" : PaymentMethods.Label(Method);

    public bool OwedToOwner => PaidFrom == Model.PaidFrom.Owner && ReimbursedOn is null;
}

/// <summary>2.6. Who paid for an expense.</summary>
public static class PaidFrom
{
    public const string Business = "business";
    public const string Owner = "owner";
    public const string NoCash = "noncash";
    public static bool IsValid(string? v) => v is Business or Owner or NoCash;

    public static string Label(string? v) => v switch
    {
        Owner => "Personal funds (owner)",
        NoCash => "No cash",
        _ => "Business",
    };
}

/// <summary>A receipt file. <see cref="RelPath"/> is relative to the receipts folder; <see cref="Folder"/> is the folder it was saved under.</summary>
public sealed class ExpenseReceipt
{
    public long Id { get; set; }
    public long ExpenseId { get; set; }
    public string FileName { get; set; } = "";
    public string RelPath { get; set; } = "";
    public string Folder { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? CreatedAt { get; set; }
}

public sealed class ExpenseCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>2.6. Counted as cost of revenue (above gross profit) on the P&amp;L, not as an operating expense.</summary>
    public bool CostOfRevenue { get; set; }
}

public sealed class Vendor
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Contact { get; set; } = "";
    public string? Address { get; set; } = "";
    public string? City { get; set; } = "";
    public string? State { get; set; } = "";
    public string? ZipCode { get; set; } = "";
    public string? Phone { get; set; } = "";
    public string? Email { get; set; } = "";
    public long? DefaultCategoryId { get; set; }
    public string? Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? CreatedAt { get; set; }

    /// <summary>Loaded when a screen needs the totals.</summary>
    public List<Expense> Expenses { get; set; } = new();

    public double TotalBilled => PyMath.Sum(Expenses, e => e.Amount);

    public double TotalPaid => PyMath.Sum(Expenses, e => e.AmountPaid);

    public double Owed => PyMath.Sum(Expenses, e => e.BalanceDue);

    public string FullAddress =>
        string.Join(", ", new[] { Address, City, State, ZipCode }.Where(p => !string.IsNullOrEmpty(p)));
}

public static class RecurringFrequency
{
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public static bool IsValid(string? f) => f is Monthly or Yearly;
}

/// <summary>How a recurring expense is created: already paid in full, or as a bill to pay.</summary>
public static class RecurringMode
{
    public const string Paid = "paid";
    public const string Bill = "bill";
    public static bool IsValid(string? m) => m is Paid or Bill;
}

/// <summary>A template that creates an expense every month or year.</summary>
public sealed class RecurringExpense
{
    public long Id { get; set; }
    public long? VendorId { get; set; }
    public long CategoryId { get; set; }
    public string Description { get; set; } = "";
    public double Amount { get; set; }
    public string Frequency { get; set; } = RecurringFrequency.Monthly;
    public DateOnly StartDate { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public DateOnly? EndDate { get; set; }
    public string Mode { get; set; } = RecurringMode.Bill;
    public string? Method { get; set; }
    public string? Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>The last date occurrences were created up to. Nothing on or before it is ever created again.</summary>
    public DateOnly? GeneratedThrough { get; set; }
    public string? CreatedAt { get; set; }

    public Vendor? Vendor { get; set; }
    public ExpenseCategory? Category { get; set; }

    public string ScheduleLabel => Frequency == RecurringFrequency.Yearly
        ? $"Yearly on {new DateOnly(2000, StartDate.Month, 1).ToString("MMM", CultureInfo.InvariantCulture)} {DayOfMonth}"
        : $"Monthly on day {DayOfMonth}";

    public string ModeLabel => Mode == RecurringMode.Paid ? "Auto-paid" : "Bill";
}
