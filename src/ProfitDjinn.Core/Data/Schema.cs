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

    /// <summary>Run at every start. Safe to run any number of times.</summary>
    public static void Ensure(Database database)
    {
        database.InTransaction((db, tx) =>
        {
            foreach (string sql in Tables) db.Execute(sql, transaction: tx);
            RunMigrations(db, tx);
            if (!db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM roles)", transaction: tx))
                Seed.FirstRun(db, tx);
            Seed.EnsureSettings(db, tx);
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
    }

    internal static HashSet<string> Columns(SqliteConnection db, SqliteTransaction? tx, string table) =>
        db.Query<string>($"SELECT name FROM pragma_table_info('{table}')", transaction: tx)
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
