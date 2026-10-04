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
    public static Func<AppPage> Bill(MainWindow s, long workOrderId, string? label = null) => Wo(s, () => new BillPage(s, workOrderId, label));

    // ---- 2.4 recurring invoices. No on/off switch: they only appear once a schedule exists.
    public static Func<AppPage> RecurringInvoices(MainWindow s) => () => new RecurringInvoicesPage(s);
    public static Func<AppPage> NewRecurringInvoice(MainWindow s, long? customerId = null) => () => new RecurringInvoiceFormPage(s, null, customerId);
    public static Func<AppPage> EditRecurringInvoice(MainWindow s, long id) => () => new RecurringInvoiceFormPage(s, id, null);
    /// <summary>A schedule's upcoming invoice, shown like an invoice. Without a date, its next one.</summary>
    public static Func<AppPage> Upcoming(MainWindow s, long recurringId, DateOnly? date = null) => () => new UpcomingInvoicePage(s, recurringId, date);

    // 2.5: work orders can be switched off (Settings > Features); these then open the Dashboard.
    private static Func<AppPage> Wo(MainWindow s, Func<AppPage> open) => () => s.Store.WorkOrders.Enabled ? open() : new DashboardPage(s);

    public static Func<AppPage> WorkOrders(MainWindow s, bool all = false, string search = "") => Wo(s, () => new WorkOrdersPage(s, all, search));
    /// <summary>The customer's work order tab (created on first visit, as in 1.x).</summary>
    public static Func<AppPage> WorkOrder(MainWindow s, long customerId) => Wo(s, () => new WorkOrderPage(s, customerId));

    public static Func<AppPage> Revenue(MainWindow s, int? year = null, bool all = false) => () => new RevenuePage(s, year, all);
    /// <summary>Settings, scrolled to a category (e.g. "workorders").</summary>
    public static Func<AppPage> Settings(MainWindow s, string? category = null) => () => new SettingsPage(s, category);
    public static Func<AppPage> Backup(MainWindow s) => () => new BackupPage(s);

    // ---- 2.2 Expenses. Every one is guarded: with Expenses turned off in Settings, these open
    // the Dashboard instead, which also covers Back/Forward history and --page.
    private static Func<AppPage> Exp(MainWindow s, Func<AppPage> open) => () => s.Store.Expenses.Enabled ? open() : new DashboardPage(s);

    public static Func<AppPage> Vendors(MainWindow s, string search = "", bool inactive = false) => Exp(s, () => new VendorsPage(s, search, inactive));
    public static Func<AppPage> Vendor(MainWindow s, long id) => Exp(s, () => new VendorDetailPage(s, id));
    public static Func<AppPage> NewVendor(MainWindow s) => Exp(s, () => new VendorFormPage(s, null));
    public static Func<AppPage> EditVendor(MainWindow s, long id) => Exp(s, () => new VendorFormPage(s, id));
    public static Func<AppPage> Expenses(MainWindow s, ExpenseFilter filter = ExpenseFilter.All, string search = "", long? category = null) =>
        Exp(s, () => new ExpensesPage(s, filter, search, category));
    public static Func<AppPage> Expense(MainWindow s, long id) => Exp(s, () => new ExpenseDetailPage(s, id));
    public static Func<AppPage> NewExpense(MainWindow s, long? vendorId = null) => Exp(s, () => new ExpenseFormPage(s, null, vendorId));
    public static Func<AppPage> EditExpense(MainWindow s, long id) => Exp(s, () => new ExpenseFormPage(s, id, null));
    public static Func<AppPage> Recurring(MainWindow s) => Exp(s, () => new RecurringPage(s));
    public static Func<AppPage> NewRecurring(MainWindow s) => Exp(s, () => new RecurringFormPage(s, null));
    public static Func<AppPage> EditRecurring(MainWindow s, long id) => Exp(s, () => new RecurringFormPage(s, id));
    public static Func<AppPage> ExpenseCategories(MainWindow s) => Exp(s, () => new ExpenseCategoriesPage(s));
    public static Func<AppPage> Profit(MainWindow s, int? year = null, ProfitBasis basis = ProfitBasis.Cash) => Exp(s, () => new ProfitLossPage(s, year, basis));

    /// <summary>
    /// A sidebar key, or "name:id" for one record (customer:3, invoice:7, workorder:3 by customer,
    /// bill:2 by work order, upcoming:4 by recurring invoice). The --page start argument uses this.
    /// </summary>
    public static Func<AppPage> ForNav(MainWindow s, string key)
    {
        int colon = key.IndexOf(':');
        if (colon > 0 && key[..colon] == "settings") return Settings(s, key[(colon + 1)..]);   // settings:expenses
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
                "vendor" => Vendor(s, id),
                "editvendor" => EditVendor(s, id),
                "expense" => Expense(s, id),
                "editexpense" => EditExpense(s, id),
                "newexpense" => NewExpense(s, id),
                "editrecurring" => EditRecurring(s, id),
                "newrecurringinvoice" => NewRecurringInvoice(s, id),
                "editrecurringinvoice" => EditRecurringInvoice(s, id),
                "upcoming" => Upcoming(s, id),
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
        "settings" => Settings(s),
        "backup" => Backup(s),
        "gallery" => Gallery(s),
        "vendors" => Vendors(s),
        "newvendor" => NewVendor(s),
        "expenses" => Expenses(s),
        "newexpense" => NewExpense(s),
        "recurring" => Recurring(s),
        "recurringinvoices" => RecurringInvoices(s),
        "newrecurringinvoice" => NewRecurringInvoice(s),
        "newrecurring" => NewRecurring(s),
        "expensecategories" => ExpenseCategories(s),
        "profit" => Profit(s),
        "profitaccrual" => Profit(s, null, ProfitBasis.Accrual),
        _ => Dashboard(s),
    };
}
