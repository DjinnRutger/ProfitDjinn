using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>Visible when the value is a non-empty string or any other non-null object.</summary>
public sealed class HasValueToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            _ => true,
        };
        return has ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Upper-cases text (CSS text-transform: uppercase).</summary>
public sealed class UpperCase : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string ?? "").ToUpperInvariant();
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
