using ProfitDjinn.App.Pages;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Shell;

/// <summary>
/// Every screen and how to open it. Pages call these instead of constructing each other, so
/// the history can rebuild any page later (Back reopens it with fresh data).
/// </summary>
public static class Routes
{
    public static Func<AppPage> Dashboard(MainWindow s) => () => new DashboardPage(s);
    public static Func<AppPage> Gallery(MainWindow s) => () => new GalleryPage(s);

    public static Func<AppPage> Customers(MainWindow s) => () => new PlaceholderPage(s, "customers");
    public static Func<AppPage> Customer(MainWindow s, long id) => () => new PlaceholderPage(s, "customers");
    public static Func<AppPage> Invoices(MainWindow s, InvoiceFilter filter = InvoiceFilter.All) => () => new PlaceholderPage(s, "invoices");
    public static Func<AppPage> Invoice(MainWindow s, long id) => () => new PlaceholderPage(s, "invoices");
    public static Func<AppPage> WorkOrders(MainWindow s) => () => new PlaceholderPage(s, "workorders");
    public static Func<AppPage> Revenue(MainWindow s) => () => new PlaceholderPage(s, "revenue");

    public static Func<AppPage> ForNav(MainWindow s, string key) => key switch
    {
        "dashboard" => Dashboard(s),
        "customers" => Customers(s),
        "invoices" => Invoices(s),
        "workorders" => WorkOrders(s),
        "revenue" => Revenue(s),
        "gallery" => Gallery(s),
        _ => () => new PlaceholderPage(s, key),
    };
}
