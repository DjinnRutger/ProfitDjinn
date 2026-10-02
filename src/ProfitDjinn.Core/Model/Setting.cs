using System.Globalization;
using System.Text.Json;

namespace ProfitDjinn.Core.Model;

/// <summary>One row of the settings table, as Admin > Settings shows it.</summary>
public sealed class Setting
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public string Type { get; set; } = "text";
    public string? Description { get; set; }
    public string? Category { get; set; } = "general";

    /// <summary>JSON array of choices for a "select" setting.</summary>
    public string? Options { get; set; }

    public IReadOnlyList<string> OptionList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Options)) return Array.Empty<string>();
            try { return JsonSerializer.Deserialize<List<string>>(Options) ?? new List<string>(); }
            catch (JsonException) { return Array.Empty<string>(); }
        }
    }

    /// <summary>1.x <c>get_typed_value</c> for booleans: "true", "1" or "yes".</summary>
    public static bool AsBool(string? value) => (value ?? "").Trim().ToLowerInvariant() is "true" or "1" or "yes";

    /// <summary>1.x <c>get_typed_value</c> for numbers: int, else float, else 0.</summary>
    public static double AsNumber(string? value) =>
        double.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
}
