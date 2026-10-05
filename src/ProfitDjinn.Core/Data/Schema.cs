using Dapper;
using Microsoft.Data.Sqlite;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// Creates and upgrades app.db so that it matches what the 1.x (Flask) build creates, table
/// for table and column for column. The DDL below is what SQLAlchemy's create_all() emits;
/// it was dumped from a fresh 1.0.0-beta database. Keep it byte-for-byte compatible: the 1.x
/// build must still be able to open a file 2.0 has touched.
///
/// Startup order is the same as 1.x: create missing tables, add hand-migrated columns, seed
/// an empty file, then insert any settings and permissions that are missing.
/// </summary>
public static class Schema
{
    private static readonly string[] Tables =
    {
        """
        CREATE TABLE IF NOT EXISTS permissions (
        	id INTEGER NOT NULL,
        	name VARCHAR(128) NOT NULL,
        	description VARCHAR(255),
        	PRIMARY KEY (id),
        	UNIQUE (name)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS roles (
        	id INTEGER NOT NULL,
        	name VARCHAR(64) NOT NULL,
        	description VARCHAR(255),
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	UNIQUE (name)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS settings (
        	id INTEGER NOT NULL,
        	"key" VARCHAR(128) NOT NULL,
        	value TEXT,
        	type VARCHAR(32) NOT NULL,
        	description VARCHAR(255),
        	category VARCHAR(64),
        	options TEXT,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_settings_key ON settings (\"key\")",
        "CREATE INDEX IF NOT EXISTS ix_settings_category ON settings (category)",
        """
        CREATE TABLE IF NOT EXISTS customers (
        	id INTEGER NOT NULL,
        	name VARCHAR(200) NOT NULL,
        	attn VARCHAR(200),
        	address VARCHAR(300),
        	city VARCHAR(100),
        	state VARCHAR(50),
        	zip_code VARCHAR(20),
        	phone VARCHAR(50),
        	email VARCHAR(200),
        	notes TEXT,
        	is_active BOOLEAN NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_customers_name ON customers (name)",
        """
        CREATE TABLE IF NOT EXISTS service_items (
        	id INTEGER NOT NULL,
        	description VARCHAR(500) NOT NULL,
        	price FLOAT NOT NULL,
        	is_active BOOLEAN NOT NULL,
        	PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS role_permissions (
        	role_id INTEGER NOT NULL,
        	permission_id INTEGER NOT NULL,
        	PRIMARY KEY (role_id, permission_id),
        	FOREIGN KEY(role_id) REFERENCES roles (id),
        	FOREIGN KEY(permission_id) REFERENCES permissions (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS users (
        	id INTEGER NOT NULL,
        	username VARCHAR(64) NOT NULL,
        	email VARCHAR(120) NOT NULL,
        	password_hash VARCHAR(512) NOT NULL,
        	is_active BOOLEAN NOT NULL,
        	is_admin BOOLEAN NOT NULL,
        	role_id INTEGER,
        	theme VARCHAR(32) NOT NULL,
        	created_at DATETIME,
        	last_login DATETIME,
        	PRIMARY KEY (id),
        	FOREIGN KEY(role_id) REFERENCES roles (id)
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email ON users (email)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_users_username ON users (username)",
        """
        CREATE TABLE IF NOT EXISTS invoices (
        	id INTEGER NOT NULL,
        	invoice_number VARCHAR(50) NOT NULL,
        	customer_id INTEGER NOT NULL,
        	date DATE NOT NULL,
        	notes TEXT,
        	term1 VARCHAR(300),
        	term2 VARCHAR(300),
        	paid BOOLEAN NOT NULL,
        	paid_date DATE,
        	credit_applied FLOAT NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	FOREIGN KEY(customer_id) REFERENCES customers (id)
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_invoices_invoice_number ON invoices (invoice_number)",
        """
        CREATE TABLE IF NOT EXISTS work_orders (
        	id INTEGER NOT NULL,
        	customer_id INTEGER NOT NULL,
        	number VARCHAR(50) NOT NULL,
        	notes TEXT,
        	is_active BOOLEAN NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	UNIQUE (customer_id),
        	FOREIGN KEY(customer_id) REFERENCES customers (id)
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_work_orders_number ON work_orders (number)",
        """
        CREATE TABLE IF NOT EXISTS audit_logs (
        	id INTEGER NOT NULL,
        	user_id INTEGER,
        	action VARCHAR(64) NOT NULL,
        	resource VARCHAR(64),
        	resource_id INTEGER,
        	details TEXT,
        	ip_address VARCHAR(45),
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	FOREIGN KEY(user_id) REFERENCES users (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_audit_logs_created_at ON audit_logs (created_at)",
        """
        CREATE TABLE IF NOT EXISTS invoice_lines (
        	id INTEGER NOT NULL,
        	invoice_id INTEGER NOT NULL,
        	description VARCHAR(500) NOT NULL,
        	quantity FLOAT NOT NULL,
        	amount FLOAT NOT NULL,
        	PRIMARY KEY (id),
        	FOREIGN KEY(invoice_id) REFERENCES invoices (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payments (
        	id INTEGER NOT NULL,
        	invoice_id INTEGER NOT NULL,
        	customer_id INTEGER NOT NULL,
        	amount FLOAT NOT NULL,
        	method VARCHAR(30) NOT NULL,
        	check_number VARCHAR(50),
        	date DATE NOT NULL,
        	notes TEXT,
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	FOREIGN KEY(invoice_id) REFERENCES invoices (id),
        	FOREIGN KEY(customer_id) REFERENCES customers (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS work_order_lines (
        	id INTEGER NOT NULL,
        	work_order_id INTEGER NOT NULL,
        	project_label VARCHAR(120),
        	description VARCHAR(500) NOT NULL,
        	line_type VARCHAR(20) NOT NULL,
        	status VARCHAR(20) NOT NULL,
        	date_performed DATE,
        	quantity FLOAT NOT NULL,
        	rate FLOAT NOT NULL,
        	amount FLOAT NOT NULL,
        	no_charge BOOLEAN NOT NULL,
        	internal_note TEXT,
        	invoice_id INTEGER,
        	billed_at DATE,
        	created_at DATETIME,
        	PRIMARY KEY (id),
        	FOREIGN KEY(work_order_id) REFERENCES work_orders (id),
        	FOREIGN KEY(invoice_id) REFERENCES invoices (id) ON DELETE SET NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_work_order_lines_status ON work_order_lines (status)",
    };

    /// <summary>
    /// 2.2 Expenses tables. They have no 1.x equivalent: the 1.x build ignores tables it does
    /// not know, so adding them keeps the file openable by 1.x. Same conventions as the 1.x
    /// tables (no AUTOINCREMENT, money FLOAT, DATE as YYYY-MM-DD text, BOOLEAN 0/1).
    /// </summary>
    private static readonly string[] ExpenseTables =
    {
        """
        CREATE TABLE IF NOT EXISTS vendors (
        	id INTEGER NOT NULL,
        	name VARCHAR(200) NOT NULL,
        	contact VARCHAR(200),
        	address VARCHAR(300),
        	city VARCHAR(100),
        	state VARCHAR(50),
        	zip_code VARCHAR(20),
        	phone VARCHAR(50),
        	email VARCHAR(200),
        	default_category_id INTEGER,
        	notes TEXT,
        	is_active BOOLEAN NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_vendors_name ON vendors (name)",
        """
        CREATE TABLE IF NOT EXISTS expense_categories (
        	id INTEGER NOT NULL,
        	name VARCHAR(100) NOT NULL,
        	is_active BOOLEAN NOT NULL,
        	sort_order INTEGER NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS expenses (
        	id INTEGER NOT NULL,
        	vendor_id INTEGER,
        	category_id INTEGER NOT NULL,
        	date DATE NOT NULL,
        	due_date DATE,
        	description VARCHAR(500) NOT NULL,
        	reference VARCHAR(100),
        	amount FLOAT NOT NULL,
        	notes TEXT,
        	recurring_id INTEGER,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_expenses_date ON expenses (date)",
        "CREATE INDEX IF NOT EXISTS ix_expenses_vendor_id ON expenses (vendor_id)",
        "CREATE INDEX IF NOT EXISTS ix_expenses_category_id ON expenses (category_id)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_expenses_recurring ON expenses (recurring_id, date) WHERE recurring_id IS NOT NULL",
        """
        CREATE TABLE IF NOT EXISTS expense_payments (
        	id INTEGER NOT NULL,
        	expense_id INTEGER NOT NULL,
        	amount FLOAT NOT NULL,
        	method VARCHAR(30) NOT NULL,
        	check_number VARCHAR(50),
        	date DATE NOT NULL,
        	notes TEXT,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_expense_payments_expense_id ON expense_payments (expense_id)",
        """
        CREATE TABLE IF NOT EXISTS expense_receipts (
        	id INTEGER NOT NULL,
        	expense_id INTEGER NOT NULL,
        	file_name VARCHAR(255) NOT NULL,
        	rel_path VARCHAR(500) NOT NULL,
        	folder VARCHAR(500) NOT NULL,
        	size_bytes INTEGER NOT NULL,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_expense_receipts_expense_id ON expense_receipts (expense_id)",
        """
        CREATE TABLE IF NOT EXISTS recurring_expenses (
        	id INTEGER NOT NULL,
        	vendor_id INTEGER,
        	category_id INTEGER NOT NULL,
        	description VARCHAR(500) NOT NULL,
        	amount FLOAT NOT NULL,
        	frequency VARCHAR(10) NOT NULL,
        	start_date DATE NOT NULL,
        	day_of_month INTEGER NOT NULL,
        	end_date DATE,
        	mode VARCHAR(10) NOT NULL,
        	method VARCHAR(30),
        	notes TEXT,
        	is_active BOOLEAN NOT NULL,
        	generated_through DATE,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
    };

    /// <summary>
    /// 2.4 recurring invoices. New tables only, like the expense tables, so 1.x still opens the
    /// file. <c>recurring_invoice_runs</c> records which date of which schedule became which
    /// invoice; its unique index is what stops a date from ever being invoiced twice, and it
    /// avoids adding a column to 1.x's <c>invoices</c> table. <c>interval</c>,
    /// <c>interval_count</c> and <c>collection_method</c> use Stripe's names and values (see
    /// docs/stripe-readiness.md).
    /// </summary>
    private static readonly string[] RecurringInvoiceTables =
    {
        """
        CREATE TABLE IF NOT EXISTS recurring_invoices (
        	id INTEGER NOT NULL,
        	customer_id INTEGER NOT NULL,
        	interval VARCHAR(10) NOT NULL,
        	interval_count INTEGER NOT NULL DEFAULT 1,
        	start_date DATE NOT NULL,
        	day_of_month INTEGER NOT NULL,
        	end_date DATE,
        	notes TEXT,
        	term1 VARCHAR(300),
        	term2 VARCHAR(300),
        	collection_method VARCHAR(30) NOT NULL DEFAULT 'send_invoice',
        	is_active BOOLEAN NOT NULL,
        	generated_through DATE,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_recurring_invoices_customer_id ON recurring_invoices (customer_id)",
        """
        CREATE TABLE IF NOT EXISTS recurring_invoice_lines (
        	id INTEGER NOT NULL,
        	recurring_id INTEGER NOT NULL,
        	position INTEGER NOT NULL,
        	description VARCHAR(500) NOT NULL,
        	quantity FLOAT NOT NULL,
        	amount FLOAT NOT NULL,
        	bill_period BOOLEAN NOT NULL DEFAULT 0,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_recurring_invoice_lines_recurring_id ON recurring_invoice_lines (recurring_id)",
        """
        CREATE TABLE IF NOT EXISTS recurring_invoice_runs (
        	id INTEGER NOT NULL,
        	recurring_id INTEGER NOT NULL,
        	date DATE NOT NULL,
        	invoice_id INTEGER,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recurring_invoice_runs ON recurring_invoice_runs (recurring_id, date)",
        "CREATE INDEX IF NOT EXISTS ix_recurring_invoice_runs_invoice_id ON recurring_invoice_runs (invoice_id)",
    };

    /// <summary>
    /// 2.5 service dates for invoice lines. A side table rather than columns on 1.x's
    /// invoice_lines, so 1.x keeps the exact table it knows. Keyed by line id and checked against
    /// the invoice id and description on load, so a line id 1.x frees and reuses never inherits
    /// dates; rows whose line is gone or changed are removed at start.
    /// </summary>
    private static readonly string[] ServiceDateTables =
    {
        """
        CREATE TABLE IF NOT EXISTS invoice_line_service (
        	line_id INTEGER NOT NULL,
        	invoice_id INTEGER NOT NULL,
        	description VARCHAR(500) NOT NULL,
        	service_start DATE,
        	service_end DATE,
        	PRIMARY KEY (line_id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_invoice_line_service_invoice_id ON invoice_line_service (invoice_id)",
    };

    /// <summary>
    /// 2.6 tables. Mileage details for an expense; bank, card, personal and payment-processor
    /// accounts with their transactions (Bank Accounts, off by default); and a side table that
    /// links an invoice payment to the account it was deposited to (payments is a 1.x table, so
    /// it gets no column). The P&amp;L never reads the bank tables: transfers, owner contributions
    /// and draws are not income or expense.
    /// </summary>
    private static readonly string[] Tables26 =
    {
        """
        CREATE TABLE IF NOT EXISTS expense_mileage (
        	expense_id INTEGER NOT NULL,
        	miles FLOAT NOT NULL,
        	rate FLOAT NOT NULL,
        	PRIMARY KEY (expense_id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS bank_accounts (
        	id INTEGER NOT NULL,
        	name VARCHAR(100) NOT NULL,
        	kind VARCHAR(20) NOT NULL,
        	opening_balance FLOAT NOT NULL DEFAULT 0,
        	opening_date DATE NOT NULL,
        	is_active BOOLEAN NOT NULL DEFAULT 1,
        	reconciled_through DATE,
        	reconciled_balance FLOAT,
        	notes TEXT,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS bank_transactions (
        	id INTEGER NOT NULL,
        	account_id INTEGER NOT NULL,
        	date DATE NOT NULL,
        	description VARCHAR(300) NOT NULL,
        	amount FLOAT NOT NULL,
        	kind VARCHAR(30) NOT NULL,
        	status VARCHAR(12) NOT NULL DEFAULT 'pending',
        	transfer_id INTEGER,
        	reference VARCHAR(100),
        	notes TEXT,
        	invoice_payment_id INTEGER,
        	expense_payment_id INTEGER,
        	expense_id INTEGER,
        	created_at DATETIME,
        	PRIMARY KEY (id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_bank_transactions_account ON bank_transactions (account_id, date)",
        "CREATE INDEX IF NOT EXISTS ix_bank_transactions_invoice_payment ON bank_transactions (invoice_payment_id)",
        "CREATE INDEX IF NOT EXISTS ix_bank_transactions_expense_payment ON bank_transactions (expense_payment_id)",
        """
        CREATE TABLE IF NOT EXISTS payment_accounts (
        	payment_id INTEGER NOT NULL,
        	account_id INTEGER NOT NULL,
        	bank_transaction_id INTEGER NOT NULL,
        	PRIMARY KEY (payment_id)
        )
        """,
    };

    /// <summary>Run at every start. Safe to run any number of times.</summary>
    public static void Ensure(Database database)
    {
        database.InTransaction((db, tx) =>
        {
            foreach (string sql in Tables) db.Execute(sql, transaction: tx);
            // Seeded once, when the table is first created; never again, even if the user later
            // deletes every starter category.
            bool newCategories = !TableExists(db, tx, "expense_categories");
            foreach (string sql in ExpenseTables) db.Execute(sql, transaction: tx);
            if (newCategories) Seed.ExpenseCategories(db, tx);
            foreach (string sql in RecurringInvoiceTables) db.Execute(sql, transaction: tx);
            foreach (string sql in ServiceDateTables) db.Execute(sql, transaction: tx);
            foreach (string sql in Tables26) db.Execute(sql, transaction: tx);
            db.Execute("""
                DELETE FROM invoice_line_service WHERE NOT EXISTS
                    (SELECT 1 FROM invoice_lines l WHERE l.id = invoice_line_service.line_id AND l.invoice_id = invoice_line_service.invoice_id
                        AND l.description = invoice_line_service.description)
                """, transaction: tx);
            RunMigrations(db, tx);
            if (!db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM roles)", transaction: tx))
                Seed.FirstRun(db, tx);
            Seed.EnsureSettings(db, tx);
            Seed.ClearOldSamples(db, tx);
            Seed.EnsurePermissions(db, tx);
            Seed.ApplyBrandDefaults(db, tx);
        });
    }

    /// <summary>The two hand-written migrations from 1.x <c>_run_migrations()</c>.</summary>
    private static void RunMigrations(SqliteConnection db, SqliteTransaction tx)
    {
        var userCols = Columns(db, tx, "users");
        if (!userCols.Contains("theme"))
        {
            db.Execute("ALTER TABLE users ADD COLUMN theme VARCHAR(32) NOT NULL DEFAULT 'light'", transaction: tx);
            if (userCols.Contains("dark_mode"))
                db.Execute("UPDATE users SET theme='dark' WHERE dark_mode=1", transaction: tx);
        }

        if (!Columns(db, tx, "invoices").Contains("credit_applied"))
            db.Execute("ALTER TABLE invoices ADD COLUMN credit_applied FLOAT NOT NULL DEFAULT 0", transaction: tx);

        // 2.5 (a 2.4 table, not a 1.x one).
        if (!Columns(db, tx, "recurring_invoice_lines").Contains("bill_period"))
            db.Execute("ALTER TABLE recurring_invoice_lines ADD COLUMN bill_period BOOLEAN NOT NULL DEFAULT 0", transaction: tx);

        // 2.6 (2.2 tables, not 1.x ones): cost of revenue, and who paid an expense.
        if (!Columns(db, tx, "expense_categories").Contains("cost_of_revenue"))
            db.Execute("ALTER TABLE expense_categories ADD COLUMN cost_of_revenue BOOLEAN NOT NULL DEFAULT 0", transaction: tx);
        var paymentCols = Columns(db, tx, "expense_payments");
        if (!paymentCols.Contains("paid_from"))
            db.Execute("ALTER TABLE expense_payments ADD COLUMN paid_from VARCHAR(10) NOT NULL DEFAULT 'business'", transaction: tx);
        if (!paymentCols.Contains("account_id"))
            db.Execute("ALTER TABLE expense_payments ADD COLUMN account_id INTEGER", transaction: tx);
        if (!paymentCols.Contains("reimbursed_on"))
            db.Execute("ALTER TABLE expense_payments ADD COLUMN reimbursed_on DATE", transaction: tx);
    }

    private static bool TableExists(SqliteConnection db, SqliteTransaction tx, string table) =>
        db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @table)", new { table }, tx);

    internal static HashSet<string> Columns(SqliteConnection db, SqliteTransaction? tx, string table) =>
        db.Query<string>($"SELECT name FROM pragma_table_info('{table}')", transaction: tx)
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
