using System.Globalization;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

public enum NoticeKind { Success, Info, Warning, Danger }

/// <summary>
/// The message shown at the top of the page after an action, like 1.x's flash messages.
/// The wording is kept from 1.x.
/// </summary>
public sealed record Notice(string Message, NoticeKind Kind)
{
    public static Notice Success(string m) => new(m, NoticeKind.Success);
    public static Notice Info(string m) => new(m, NoticeKind.Info);
    public static Notice Warning(string m) => new(m, NoticeKind.Warning);
}

/// <summary>The result of an action that created something: its id and the message to show.</summary>
public sealed record Created(long Id, Notice Notice);

/// <summary>Form fields that failed validation, by field name, plus a summary.</summary>
public sealed class ValidationException : UserFacingException
{
    public IReadOnlyDictionary<string, string> Fields { get; }

    public ValidationException(IReadOnlyDictionary<string, string> fields, string? summary = null)
        : base(summary ?? string.Join(" ", fields.Values.Distinct()))
    {
        Fields = fields;
    }
}

/// <summary>Collects field errors, then throws them together.</summary>
internal sealed class Checks
{
    private readonly Dictionary<string, string> _errors = new();

    public const string Required = "This field is required.";

    public void Add(string field, string message)
    {
        if (!_errors.ContainsKey(field)) _errors[field] = message;
    }

    public void RequireText(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, Required);
        else MaxLength(field, value, maxLength);
    }

    public void MaxLength(string field, string? value, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
            Add(field, $"Field cannot be longer than {maxLength} characters.");
    }

    public void ThrowIfAny(string? summary = null)
    {
        if (_errors.Count > 0) throw new ValidationException(_errors, summary);
    }
}

internal static class Fmt
{
    /// <summary>Python's f"{x:.2f}": no thousands separator. Used in the 1.x messages.</summary>
    public static string F2(double x) => PyMath.Round(x, 2).ToString("F2", CultureInfo.InvariantCulture);

    public static string Plural(int n, string word) => n == 1 ? word : word + "s";
}
