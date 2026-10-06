using Dapper;
using Microsoft.Data.Sqlite;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// The rows a database needs before anything works. Values are copied from the 1.x seed
/// (app/__init__.py) so a file made by either build looks the same to the other. Roles and
/// permissions are no longer used by 2.0, but a 1.x build cannot start without them.
/// </summary>
internal static class Seed
{
    private static readonly (string Name, string Description)[] Permissions =
    {
        ("admin.full_access", "Full admin panel access"),
        ("dashboard.view", "View dashboard"),
        ("users.view", "View user list"),
        ("users.create", "Create users"),
        ("users.edit", "Edit users"),
        ("users.delete", "Delete users"),
        ("roles.view", "View roles"),
        ("roles.create", "Create roles"),
        ("roles.edit", "Edit roles"),
        ("roles.delete", "Delete roles"),
        ("settings.view", "View settings"),
        ("settings.edit", "Edit settings"),
        ("audit.view", "View audit log"),
        ("database.view", "View database management"),
        ("database.backup", "Download database backups"),
        ("database.configure", "Configure database connection"),
        ("customers.view", "View customers"),
        ("customers.create", "Create customers"),
        ("customers.edit", "Edit customers"),
        ("customers.delete", "Delete customers"),
        ("invoices.view", "View invoices"),
        ("invoices.create", "Create invoices"),
        ("invoices.edit", "Edit invoices"),
        ("invoices.delete", "Delete invoices"),
        ("items.view", "View service items"),
        ("items.create", "Create service items"),
        ("items.edit", "Edit service items"),
        ("items.delete", "Delete service items"),
        ("workorders.view", "View work orders"),
        ("workorders.create", "Add work order lines"),
        ("workorders.edit", "Edit work order lines"),
        ("workorders.delete", "Delete work order lines"),
    };

    /// <summary>1.x <c>_ensure_permissions()</c> adds these to older files and grants them to full-access roles.</summary>
    private static readonly string[] LaterPermissionPrefixes = { "customers.", "invoices.", "items.", "workorders." };

    internal sealed record SettingDef(string Key, string Value, string Type, string Description, string Category, string? Options);

    /// <summary>Inserted only into an empty database (1.x <c>_seed_database</c>).</summary>
    private static readonly SettingDef[] FirstRunSettings =
    {
        new("app_name", "ProfitDjinn", "text", "Application display name", "general", null),
        new("app_tagline", "Rub the Lamp, Send the Invoice, Count the Gold!", "text", "Tagline shown on the login page", "general", null),
        new("app_icon", "bi-lightning-charge-fill", "text", "Bootstrap Icons class for the sidebar logo", "appearance", null),
        new("footer_text", "ProfitDjinn", "text", "Footer copyright text", "general", null),
        new("primary_color", "#2563eb", "color", "Primary brand/accent colour", "appearance", null),
        new("default_theme", "light", "select", "Default colour theme for new users", "appearance", "[\"light\",\"dark\",\"terminal\"]"),
        new("allow_registration", "false", "boolean", "Allow new visitors to self-register", "security", null),
        new("maintenance_mode", "false", "boolean", "Show maintenance page to non-admin users", "general", null),
        new("items_per_page", "20", "number", "Rows shown per page in data tables", "general", null),
        new("session_timeout", "480", "number", "Session idle timeout in minutes (0 = never)", "security", null),
    };

    /// <summary>Inserted whenever missing (1.x <c>_ensure_invoice_settings</c>, plus the 2.0 additions at the end).</summary>
    internal static readonly SettingDef[] EnsuredSettings =
    {
        new("company_name", "", "text", "Your name / company name on invoices", "invoices", null),
        new("company_address", "", "text", "Street address for invoice header", "invoices", null),
        new("company_city", "", "text", "City for invoice header", "invoices", null),
        new("company_state", "", "text", "State for invoice header", "invoices", null),
        new("company_zip", "", "text", "ZIP code for invoice header", "invoices", null),
        new("company_email", "", "text", "Email shown on invoices", "invoices", null),
        new("company_phone", "", "text", "Phone shown on invoices", "invoices", null),
        new("invoice_prefix", "INV", "text", "Invoice number prefix", "invoices", null),
        new("invoice_next_number", "1001", "number", "Next invoice number sequence start", "invoices", null),
        new("invoice_term1", "Payment Terms: Due within 30 days", "text", "Default payment terms line 1", "invoices", null),
        new("invoice_term2", "", "text", "Default payment terms line 2", "invoices", null),
        new("ui_font_scale", "1.0", "select", "Site-wide text size (affects all pages)", "ui", "[\"0.80\", \"0.85\", \"0.90\", \"0.95\", \"1.0\", \"1.05\", \"1.10\", \"1.15\", \"1.20\", \"1.25\"]"),
        new("login_logo_layout", "left", "select", "Login page: logo/name/tagline position", "login", "[\"top\", \"left\"]"),
        new("login_logo", "login_logo.png", "text", "Custom login logo filename (auto-managed)", "login", null),
        new("app_icon_img", "app_icon.png", "text", "Custom sidebar icon image (auto-managed)", "appearance", null),
        new("workorder_prefix", "WO", "text", "Work order number prefix", "workorders", null),
        new("workorder_next_number", "1001", "number", "Next work order number sequence start", "workorders", null),
        new("default_hourly_rate", "0.00", "number", "Default hourly labor rate for work order entries", "workorders", null),
        // 2.0: the optional app password. Stored as a PBKDF2 hash; empty means no password.
        new(SettingKeys.AppPasswordHash, "", "secret", "App password (set it in Settings > Security)", "security", null),
        new(SettingKeys.Theme, "", "text", "Colour theme (light, dark or terminal)", "appearance", null),
        new(SettingKeys.BackupReminderEnabled, "true", "boolean", "Remind me at start to back up my data", "backup", null),
        new(SettingKeys.BackupReminderDays, "7", "number", "Days between backup reminders", "backup", null),
        new(SettingKeys.BackupReminderLast, "", "text", "When the reminder countdown last restarted (auto-managed)", "backup", null),
        new(SettingKeys.ExpensesEnabled, "false", "boolean", "Track vendors, expenses and recurring costs", "expenses", null),
        new(SettingKeys.ReceiptsFolder, "", "text", "Folder for receipt files (empty = the data folder's receipts folder)", "expenses", null),
        new(SettingKeys.UpdateCheckEnabled, "true", "boolean", "Check GitHub once a day for a newer version", "updates", null),
        new(SettingKeys.UpdateLastCheck, "", "text", "Date of the last update check (auto-managed)", "updates", null),
        new(SettingKeys.UpdateLatestVersion, "", "text", "Latest version found on GitHub (auto-managed)", "updates", null),
        new(SettingKeys.UpdateLatestUrl, "", "text", "Release page of the latest version (auto-managed)", "updates", null),
        new(SettingKeys.DatabaseAppVersion, "", "text", "App version that last opened this database (auto-managed)", "backup", null),
        new(SettingKeys.WorkOrdersEnabled, "true", "boolean", "Work orders: log work per customer and bill it", "features", null),
        new(SettingKeys.MileageRate, "0.70", "number", "Dollars per mile for mileage expenses", "expenses", null),
        new(SettingKeys.BankingEnabled, "false", "boolean", "Bank accounts: balances, transfers and reconciliation", "features", null),
        new(SettingKeys.RevenueEnabled, "true", "boolean", "Revenue report: income by month, year and customer", "features", null),
        new(SettingKeys.ItemsEnabled, "true", "boolean", "Service items: a price list to quick-add onto invoices", "features", null),
    };

    /// <summary>
    /// 2.5. The sample values 1.x and 2.0-2.4 seeded into the business settings. They were real
    /// text, not hints, so a new user had to delete each one; Settings now shows these as
    /// placeholders instead. A setting still holding exactly its sample is cleared at start.
    /// </summary>
    internal static readonly (string Key, string Sample)[] OldSamples =
    {
        ("company_name", "Your Name"),
        ("company_address", "123 Main Street"),
        ("company_city", "Anytown"),
        ("company_state", "ST"),
        ("company_zip", "00000"),
        ("company_email", "you@example.com"),
        ("company_phone", "(555) 555-0100"),
        ("invoice_term2", "Make all checks payable to Your Name"),
    };

    internal static void ClearOldSamples(SqliteConnection db, SqliteTransaction tx)
    {
        foreach (var (key, sample) in OldSamples)
            db.Execute("UPDATE settings SET value = '' WHERE \"key\" = @key AND value = @sample", new { key, sample }, tx);
    }

    internal static void FirstRun(SqliteConnection db, SqliteTransaction tx)
    {
        string now = SqlFormat.NowUtc();
        foreach (var (name, description) in Permissions)
            db.Execute("INSERT INTO permissions (name, description) VALUES (@name, @description)", new { name, description }, tx);

        long admin = db.ExecuteScalar<long>(
            "INSERT INTO roles (name, description, created_at) VALUES ('Administrator', 'Full system access', @now); SELECT last_insert_rowid();",
            new { now }, tx);
        long standard = db.ExecuteScalar<long>(
            "INSERT INTO roles (name, description, created_at) VALUES ('Standard User', 'Basic read-only access', @now); SELECT last_insert_rowid();",
            new { now }, tx);

        db.Execute("INSERT INTO role_permissions (role_id, permission_id) SELECT @admin, id FROM permissions", new { admin }, tx);
        db.Execute("INSERT INTO role_permissions (role_id, permission_id) SELECT @standard, id FROM permissions WHERE name = 'dashboard.view'",
            new { standard }, tx);

        foreach (var s in FirstRunSettings) Insert(db, tx, s);
    }

    internal static void EnsureSettings(SqliteConnection db, SqliteTransaction tx)
    {
        var existing = db.Query<string>("SELECT \"key\" FROM settings", transaction: tx).ToHashSet();
        foreach (var s in EnsuredSettings)
            if (!existing.Contains(s.Key)) Insert(db, tx, s);
    }

    internal static void EnsurePermissions(SqliteConnection db, SqliteTransaction tx)
    {
        var existing = db.Query<string>("SELECT name FROM permissions", transaction: tx).ToHashSet();
        foreach (var (name, description) in Permissions)
        {
            if (existing.Contains(name) || !LaterPermissionPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) continue;
            long id = db.ExecuteScalar<long>(
                "INSERT INTO permissions (name, description) VALUES (@name, @description); SELECT last_insert_rowid();",
                new { name, description }, tx);
            db.Execute("""
                INSERT OR IGNORE INTO role_permissions (role_id, permission_id)
                SELECT rp.role_id, @id FROM role_permissions rp
                JOIN permissions p ON p.id = rp.permission_id WHERE p.name = 'admin.full_access'
                """, new { id }, tx);
        }
    }

    /// <summary>
    /// Old default values that were never changed are moved to the current defaults
    /// (1.x <c>_apply_brand_defaults</c>). A value the user has changed is never touched.
    /// </summary>
    internal static void ApplyBrandDefaults(SqliteConnection db, SqliteTransaction tx)
    {
        (string Key, string Old, string New)[] moves =
        {
            ("app_name", "LocalVibe", "ProfitDjinn"),
            ("app_tagline", "Your Local Network Hub", "Rub the Lamp, Send the Invoice, Count the Gold!"),
            ("footer_text", "LocalVibe — Built with Flask", "ProfitDjinn"),
            ("footer_text", "ProfitDjinn — Built with Flask", "ProfitDjinn"),
            // 1.x also moved login_logo_layout "top" to "left" on every start, which meant
            // "top" could never be kept. Dropped in 2.0.
        };
        foreach (var (key, old, @new) in moves)
            db.Execute("UPDATE settings SET value = @new WHERE \"key\" = @key AND value = @old", new { key, old, @new }, tx);
    }

    /// <summary>2.2: starter expense categories, modelled on the IRS Schedule C lines. "Other" is last.</summary>
    internal static readonly string[] StarterCategories =
    {
        "Advertising", "Car & Truck", "Contract Labor", "Insurance", "Interest & Bank Fees",
        "Legal & Professional", "Meals", "Office Expenses", "Rent/Lease", "Repairs & Maintenance",
        "Supplies", "Software & Subscriptions", "Taxes & Licenses", "Travel", "Utilities",
        "Phone & Internet", "Other",
    };

    internal static void ExpenseCategories(SqliteConnection db, SqliteTransaction tx)
    {
        string now = SqlFormat.NowUtc();
        for (int i = 0; i < StarterCategories.Length; i++)
            db.Execute("INSERT INTO expense_categories (name, is_active, sort_order, created_at) VALUES (@name, 1, @i, @now)",
                new { name = StarterCategories[i], i, now }, tx);
    }

    private static void Insert(SqliteConnection db, SqliteTransaction tx, SettingDef s) =>
        db.Execute("""
            INSERT INTO settings ("key", value, type, description, category, options)
            VALUES (@Key, @Value, @Type, @Description, @Category, @Options)
            """, s, tx);
}
