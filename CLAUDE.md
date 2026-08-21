# ProfitDjinn

Jon's invoicing and customer management app. Flask + SQLAlchemy, SQLite, runs as a
local desktop app (FlaskWebGUI window) or as a plain dev server.

General rules load from `Software Dev/CLAUDE.md` and the `~DjinnStudios` root
`CLAUDE.md`. This file is project facts only.

**Name check:** folder `ProfitDjinn`, repo `DjinnRutger/ProfitDjinn`, app display name
`ProfitDjinn` (the `app_name` setting), README `ProfitDjinn`. All four agree. It grew out
of a scaffold called **LocalVibe** — that name still appears in a few docstrings and in
`_apply_brand_defaults()`, which migrates old LocalVibe setting values forward. Don't
delete that function; existing databases depend on it.

## What it does

- **Customers** — contact record, address, notes, and derived totals (invoiced,
  outstanding, paid, account credit).
- **Work orders** — one open work order per customer. Lines are logged as work happens
  (labor with hours x rate, or flat-rate items), grouped by `project_label`, and marked
  pending / completed. Completed lines get pulled onto an invoice and stamped billed.
- **Invoices** — line items, partial payments, and account credit applied against the
  balance. PDF output via `fpdf2`.
- **Service items** — a reusable price list for common line descriptions.
- **Admin** — users, roles, permissions, dynamic settings, audit log, database
  backup/restore.

## Run it

There is no venv in the project folder — per `Software Dev/CLAUDE.md` it belongs at
`C:\Dev\venvs\ProfitDjinn\`. Create it once per machine:

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements.txt
```

Then:

```
C:\Dev\venvs\ProfitDjinn\Scripts\python run.py        # dev server, http://localhost:5000
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py    # desktop window (Edge/Chrome app mode)
```

`.env` (gitignored) supplies `SECRET_KEY`, `DATABASE_URI`, and `FLASK_ENV`. Without it
`app/config.py` falls back to `instance/app.db` and a dev secret key.

First run seeds an admin account. Credentials are in the seed block in
`app/__init__.py` — change the password before anyone else touches this.

## Architecture

Application factory in `app/__init__.py`, four config classes in `app/config.py`
(`development` / `production` / `testing` / `gui`), extensions in `app/extensions.py`.

Blueprints: `auth`, `main`, `admin`, `database_mgr`, `customers`, `invoices`, `items`,
`work_orders`. Every route is login-gated; most are permission-gated with
`@permission_required('...')` from `app/utils/decorators.py`.

`work_orders_bp` is exempted from Flask-Limiter — rapid line entry blows past the
global 50/hour default. It is still login- and permission-gated.

### Settings, not constants

Anything a user might want to change lives in the `settings` table and is read with
`get_setting()` (`app/utils/settings.py`), which is injected into every template.
Company name and address on invoices, invoice/work-order number prefixes and next
number, theme, font scale, login layout, hourly rate default — all settings. Don't
hardcode any of them.

Three bootstrap functions run inside the app context on every start and are all
idempotent: `_ensure_invoice_settings()`, `_ensure_permissions()`,
`_apply_brand_defaults()`. Add new settings and permissions there so existing
databases pick them up.

### Migrations — read this before changing a model

`migrations/` is **empty**. Flask-Migrate is installed and initialized, but
`flask db init` was never run. Schema changes are handled two ways instead:

1. `db.create_all()` on startup creates any missing table.
2. `_run_migrations()` in `app/__init__.py` hand-writes `ALTER TABLE` for new columns
   on existing tables, guarded by an inspector check.

`create_all()` will not add a column to a table that already exists. If you add a
column to a model, you must also add a guarded `ALTER TABLE` to `_run_migrations()`
or it will silently fail against Jon's live database.

## Data

`instance/app.db` is the live SQLite database with real customer and invoice data. It
also holds a dated backup file. Back it up before any schema work.

Both are gitignored, along with `password.txt`, `Images-Org/` (source artwork) and
`Old-Database-TV2/` (the legacy data that `scripts/import_old_data.py` reads).

## Building the EXE

`build.bat` drives PyInstaller through `profitdjinn.spec` and writes
`dist\ProfitDjinn\ProfitDjinn.exe`.

**`build.bat` currently expects `.venv\` inside the project folder**, which conflicts
with the `C:\Dev\venvs\` rule. It will fail as written. Unresolved — see README.

When frozen, `run_gui.py` redirects the instance folder to a writable directory next
to the EXE and generates a persistent `.secret_key` there on first launch. The
`instance\` folder must ship and stay with the EXE or the database is lost.

## Known gaps

- **No tests.** Zero test files, no pytest. The auto-push rule assumes a smoke test
  exists; here there is nothing to run. Say so rather than pushing untested.
- `migrations/` empty (above).
- `dist/` is ~74 MB of build output sitting inside OneDrive.
- The invoice PDF has only been checked by eye, not asserted against a fixture.
