using System.Globalization;

namespace ProfitDjinn.Core.Model;

public sealed class Payment
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long CustomerId { get; set; }
    public double Amount { get; set; }
    public string Method { get; set; } = "cash";
    public string? CheckNumber { get; set; } = "";
    public DateOnly Date { get; set; }
    public string? Notes { get; set; } = "";
    public string? CreatedAt { get; set; }

    public string MethodLabel => PaymentMethods.Label(Method);
}

/// <summary>The stored payment methods (1.x models/payment.py), in the order the dialog lists them.</summary>
public static class PaymentMethods
{
    /// <summary>A dialog choice only: applies the customer's account credit. Never stored on a payment.</summary>
    public const string AccountCredit = "account_credit";

    public const string Check = "check";

    public static readonly IReadOnlyList<(string Value, string Label)> All = new[]
    {
        ("cash", "Cash"),
        ("check", "Check"),
        ("credit_card", "Credit Card"),
        ("ach", "ACH"),
        ("venmo", "Venmo"),
        ("other", "Other"),
    };

    /// <summary>The display name. An unknown method is shown the way Python's str.title() would show it.</summary>
    public static string Label(string method)
    {
        foreach (var (value, label) in All)
            if (value == method) return label;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase((method ?? "").ToLowerInvariant());
    }
}
