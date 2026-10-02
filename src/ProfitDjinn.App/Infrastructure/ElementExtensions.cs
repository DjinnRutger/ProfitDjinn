using System.Windows;

namespace ProfitDjinn.App.Infrastructure;

public static class ElementExtensions
{
    /// <summary>Binds a property to a theme resource, so it follows theme changes. Returns the element for chaining.</summary>
    public static T WithResource<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
