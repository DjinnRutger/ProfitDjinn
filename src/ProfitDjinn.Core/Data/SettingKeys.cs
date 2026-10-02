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

    /// <summary>2.1. The app version that last opened this database. A different version
    /// saves an upgrade backup before touching the file.</summary>
    public const string DatabaseAppVersion = "db_app_version";
}
