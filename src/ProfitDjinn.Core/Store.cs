using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Core;

/// <summary>
/// The open database and every service that works on it. Opening it creates the file if
/// needed and brings an older one up to date, the same steps 1.x ran at every start.
/// </summary>
public sealed class Store
{
    public AppPaths Paths { get; }
    public Database Database { get; }
    public SettingsService Settings { get; }
    public CustomerService Customers { get; }
    public InvoiceService Invoices { get; }
    public ItemService Items { get; }
    public WorkOrderService WorkOrders { get; }
    public BillingService Billing { get; }
    public ReportService Reports { get; }
    public BackupService Backups { get; }
    public AppPassword Password { get; }
    public BackupReminder BackupReminder { get; }

    /// <param name="today">The clock. Tests pass a fixed date; the app passes the local date.</param>
    public Store(AppPaths paths, Func<DateOnly>? today = null)
    {
        DapperSetup.Ensure();
        today ??= () => DateOnly.FromDateTime(DateTime.Now);
        Paths = paths;
        paths.EnsureExists();
        Database = new Database(paths.DatabasePath);
        Schema.Ensure(Database);

        Settings = new SettingsService(Database);
        Customers = new CustomerService(Database);
        Invoices = new InvoiceService(Database, Settings, today);
        Items = new ItemService(Database);
        WorkOrders = new WorkOrderService(Database, Settings, today);
        Billing = new BillingService(Database, Settings, Invoices, today);
        Reports = new ReportService(Database, today);
        Backups = new BackupService(Database);
        Password = new AppPassword(Settings);
        BackupReminder = new BackupReminder(Settings, today);
    }
}
