namespace ProfitDjinn.Core.Model;

/// <summary>2.6. What kind of account: it only changes labels and the processor bridge.</summary>
public static class AccountKind
{
    public const string Checking = "checking";
    public const string Savings = "savings";
    public const string CreditCard = "credit_card";
    public const string Personal = "personal";
    public const string Processor = "processor";

    public static readonly (string Value, string Label)[] All =
    {
        (Checking, "Checking"),
        (Savings, "Savings"),
        (CreditCard, "Credit card"),
        (Personal, "Personal (owner's own account)"),
        (Processor, "Payment processor (e.g. Stripe, Square, PayPal)"),
    };

    public static bool IsValid(string? v) => All.Any(k => k.Value == v);
    public static string Label(string? v) => All.FirstOrDefault(k => k.Value == v).Label?.Split(" (")[0] ?? "Account";
}

/// <summary>2.6. What a bank transaction is. None of these is income or expense on the P&amp;L by itself.</summary>
public static class TxnKind
{
    public const string Deposit = "deposit";
    public const string Withdrawal = "withdrawal";
    public const string OwnerContribution = "owner_contribution";
    public const string OwnerDraw = "owner_draw";
    public const string Fee = "fee";
    public const string Transfer = "transfer";
    public const string CustomerPayment = "customer_payment";
    public const string ExpensePayment = "expense_payment";
    public const string Reimbursement = "reimbursement";

    /// <summary>The choices for money typed in by hand ("What is it?").</summary>
    public static readonly (string Value, string Label)[] MoneyIn = { (OwnerContribution, "Owner contribution (your own money put in)"), (Deposit, "Other deposit") };
    public static readonly (string Value, string Label)[] MoneyOut = { (Fee, "Bank or processor fee"), (OwnerDraw, "Owner draw (money taken out for yourself)"), (Withdrawal, "Other withdrawal") };

    public static string Label(string? v) => v switch
    {
        Deposit => "Deposit",
        Withdrawal => "Withdrawal",
        OwnerContribution => "Owner contribution",
        OwnerDraw => "Owner draw",
        Fee => "Fee",
        Transfer => "Transfer",
        CustomerPayment => "Customer payment",
        ExpensePayment => "Expense payment",
        Reimbursement => "Owner paid back",
        _ => "Transaction",
    };

    public static bool IsManual(string? v) => v is Deposit or Withdrawal or OwnerContribution or OwnerDraw or Fee;
}

public static class TxnStatus
{
    public const string Pending = "pending";
    public const string Cleared = "cleared";
    public const string Reconciled = "reconciled";
}

public sealed class BankAccount
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = AccountKind.Checking;
    public double OpeningBalance { get; set; }
    public DateOnly OpeningDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? ReconciledThrough { get; set; }
    public double? ReconciledBalance { get; set; }
    public string? Notes { get; set; } = "";
    public string? CreatedAt { get; set; }

    /// <summary>Filled in by BankService: everything, cleared or reconciled only, and the pending count.</summary>
    public double Balance { get; set; }
    public double ClearedBalance { get; set; }
    public int PendingCount { get; set; }

    public string KindLabel => AccountKind.Label(Kind);
    public bool IsProcessor => Kind == AccountKind.Processor;
}

public sealed class BankTransaction
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";

    /// <summary>Signed: money in is positive, money out negative.</summary>
    public double Amount { get; set; }
    public string Kind { get; set; } = TxnKind.Deposit;
    public string Status { get; set; } = TxnStatus.Pending;

    /// <summary>The id shared by the two halves of a transfer.</summary>
    public long? TransferId { get; set; }

    /// <summary>A check number, charge id or payout id.</summary>
    public string? Reference { get; set; } = "";
    public string? Notes { get; set; } = "";
    public long? InvoicePaymentId { get; set; }
    public long? ExpensePaymentId { get; set; }
    public long? ExpenseId { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Filled in for display: the invoice or expense behind a linked row, the other side of a transfer.</summary>
    public long? InvoiceId { get; set; }
    public string? OtherAccount { get; set; }

    public bool IsLinked => InvoicePaymentId is not null || ExpensePaymentId is not null;
    public bool IsReconciled => Status == TxnStatus.Reconciled;
    public bool IsPending => Status == TxnStatus.Pending;
    public string KindLabel => TxnKind.Label(Kind);
}

/// <summary>One register line with the running balance after it.</summary>
public sealed record RegisterRow(BankTransaction Txn, double Balance);
