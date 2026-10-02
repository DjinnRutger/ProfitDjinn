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

    public static Func<AppPage> Customers(MainWindow s, string search = "", bool inactive = false) => () => new CustomersPage(s, search, inactive);
    public static Func<AppPage> Customer(MainWindow s, long id) => () => new CustomerDetailPage(s, id);
    public static Func<AppPage> NewCustomer(MainWindow s) => () => new CustomerFormPage(s, null);
    public static Func<AppPage> EditCustomer(MainWindow s, long id) => () => new CustomerFormPage(s, id);

    public static Func<AppPage> Items(MainWindow s, bool inactive = false) => () => new ItemsPage(s, inactive);
    public static Func<AppPage> NewItem(MainWindow s) => () => new ItemFormPage(s, null);
    public static Func<AppPage> EditItem(MainWindow s, long id) => () => new ItemFormPage(s, id);

    public static Func<AppPage> Invoices(MainWindow s, InvoiceFilter filter = InvoiceFilter.All, string search = "") => () => new InvoicesPage(s, filter, search);
    public static Func<AppPage> Invoice(MainWindow s, long id) => () => new InvoiceDetailPage(s, id);
    public static Func<AppPage> NewInvoice(MainWindow s, long? customerId = null) => () => new InvoiceFormPage(s, null, customerId);
    public static Func<AppPage> EditInvoice(MainWindow s, long id) => () => new InvoiceFormPage(s, id, null);
    public static Func<AppPage> Bill(MainWindow s, long workOrderId, string? label = null) => () => new BillPage(s, workOrderId, label);

    public static Func<AppPage> WorkOrders(MainWindow s, bool all = false, string search = "") => () => new WorkOrdersPage(s, all, search);
    /// <summary>The customer's work order tab (created on first visit, as in 1.x).</summary>
    public static Func<AppPage> WorkOrder(MainWindow s, long customerId) => () => new WorkOrderPage(s, customerId);

    public static Func<AppPage> Revenue(MainWindow s, int? year = null, bool all = false) => () => new PlaceholderPage(s, "revenue");
    /// <summary>Settings, scrolled to a category (e.g. "workorders").</summary>
    public static Func<AppPage> Settings(MainWindow s, string? category = null) => () => new PlaceholderPage(s, "settings");

    /// <summary>
    /// A sidebar key, or "name:id" for one record (customer:3, invoice:7, workorder:3 by customer,
    /// bill:2 by work order). The --page start argument uses this.
    /// </summary>
    public static Func<AppPage> ForNav(MainWindow s, string key)
    {
        int colon = key.IndexOf(':');
        if (colon > 0 && long.TryParse(key[(colon + 1)..], out long id))
            return key[..colon] switch
            {
                "customer" => Customer(s, id),
                "editcustomer" => EditCustomer(s, id),
                "invoice" => Invoice(s, id),
                "editinvoice" => EditInvoice(s, id),
                "newinvoice" => NewInvoice(s, id),
                "workorder" => WorkOrder(s, id),
                "bill" => Bill(s, id),
                "edititem" => EditItem(s, id),
                _ => Dashboard(s),
            };
        return Named(s, key);
    }

    private static Func<AppPage> Named(MainWindow s, string key) => key switch
    {
        "newcustomer" => NewCustomer(s),
        "newinvoice" => NewInvoice(s),
        "newitem" => NewItem(s),
        "dashboard" => Dashboard(s),
        "customers" => Customers(s),
        "invoices" => Invoices(s),
        "workorders" => WorkOrders(s),
        "revenue" => Revenue(s),
        "items" => Items(s),
        "gallery" => Gallery(s),
        _ => () => new PlaceholderPage(s, key),
    };
}
