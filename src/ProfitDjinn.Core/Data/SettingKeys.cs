namespace ProfitDjinn.Core.Data;

/// <summary>Setting keys the code reads. The full list, with defaults, is in Seed.</summary>
public static class SettingKeys
{
    public const string AppName = "app_name";
    public const string AppTagline = "app_tagline";
    public const string AppIcon = "app_icon";
    public const string AppIconImage = "app_icon_img";
    public const string FooterText = "footer_text";
    public const string PrimaryColor = "primary_color";
    public const string UiFontScale = "ui_font_scale";
    public const string LoginLogo = "login_logo";
    public const string LoginLogoLayout = "login_logo_layout";

    public const string CompanyName = "company_name";
    public const string CompanyAddress = "company_address";
    public const string CompanyCity = "company_city";
    public const string CompanyState = "company_state";
    public const string CompanyZip = "company_zip";
    public const string CompanyEmail = "company_email";
    public const string CompanyPhone = "company_phone";

    public const string InvoicePrefix = "invoice_prefix";
    public const string InvoiceNextNumber = "invoice_next_number";
    public const string InvoiceTerm1 = "invoice_term1";
    public const string InvoiceTerm2 = "invoice_term2";

    public const string WorkOrderPrefix = "workorder_prefix";
    public const string WorkOrderNextNumber = "workorder_next_number";
    public const string DefaultHourlyRate = "default_hourly_rate";

    /// <summary>2.0. PBKDF2 hash of the optional app password; empty means none.</summary>
    public const string AppPasswordHash = "app_password_hash";

    /// <summary>2.0. light, dark or terminal. In 1.x the theme was per user.</summary>
    public const string Theme = "theme";

    /// <summary>2.0. Backup reminder at start: on/off, days between reminders, and the date
    /// (YYYY-MM-DD) the countdown last restarted, set when the reminder is answered or a backup is made.</summary>
    public const string BackupReminderEnabled = "backup_reminder_enabled";
    public const string BackupReminderDays = "backup_reminder_days";
    public const string BackupReminderLast = "backup_reminder_last";

    /// <summary>2.2. Expenses on/off (off by default), and where receipt files go (empty = AppPaths.ReceiptsFolder).</summary>
    public const string ExpensesEnabled = "expenses_enabled";
    public const string ReceiptsFolder = "receipts_folder";

    /// <summary>2.5. Work orders on/off (on by default). Off hides them everywhere; the data is kept.</summary>
    public const string WorkOrdersEnabled = "workorders_enabled";

    /// <summary>2.6. Dollars per mile for mileage expenses (the user keeps it at the current IRS rate).</summary>
    public const string MileageRate = "mileage_rate";

    /// <summary>2.6. Bank Accounts on/off (off by default): accounts, transfers, reconciliation.</summary>
    public const string BankingEnabled = "banking_enabled";

    /// <summary>2.7. The Revenue page on/off (on by default). Off hides the page; the dashboard keeps the figure.</summary>
    public const string RevenueEnabled = "revenue_enabled";

    /// <summary>2.7. Service items on/off (on by default). Off hides Items and the invoice quick-add; items are kept.</summary>
    public const string ItemsEnabled = "items_enabled";

    /// <summary>2.9. Vendors on/off (off by default). Expenses and purchase orders both need vendors,
    /// so Vendors also counts as on while either of them is on (VendorService.Enabled).</summary>
    public const string VendorsEnabled = "vendors_enabled";

    /// <summary>2.9. Purchase orders on/off (off by default), and their numbering (as invoices).</summary>
    public const string PurchaseOrdersEnabled = "purchase_orders_enabled";
    public const string PurchaseOrderPrefix = "po_prefix";
    public const string PurchaseOrderNextNumber = "po_next_number";

    /// <summary>2.9. Printed in the header of invoices and purchase orders: a contact line
    /// ("Jane Smith, Owner"), the services offered (one per line, printed as bullets) and a logo
    /// (the file is AppPaths.DocumentLogoPath; the setting is non-empty while one is set).</summary>
    public const string CompanyContactLine = "company_contact_line";
    public const string CompanyServices = "company_services";
    public const string DocumentLogo = "document_logo";

    /// <summary>2.3. Daily check for a newer release on GitHub (on by default), and what it last found.</summary>
    public const string UpdateCheckEnabled = "update_check_enabled";
    public const string UpdateLastCheck = "update_last_check";
    public const string UpdateLatestVersion = "update_latest_version";
    public const string UpdateLatestUrl = "update_latest_url";

    /// <summary>2.1. The app version that last opened this database. A different version
    /// saves an upgrade backup before touching the file.</summary>
    public const string DatabaseAppVersion = "db_app_version";
}
