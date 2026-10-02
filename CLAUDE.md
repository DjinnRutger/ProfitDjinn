# ProfitDjinn

Jon's invoicing and customer management app. Flask + SQLAlchemy, SQLite, running in an
Edge WebView2 desktop window — or as a plain dev server in a browser.

Private notes (repo history, personal setup) are in the gitignored `CLAUDE.local.md`.
This file is public: technical project facts only.

**Name check:** folder `ProfitDjinn`, repo `DjinnRutger/ProfitDjinn`, app display name
`ProfitDjinn` (the `app_name` setting), README `ProfitDjinn`. All four agree. It grew out
of a scaffold called **LocalVibe**; that name now survives only inside
`_apply_brand_defaults()`, which migrates old LocalVibe setting values forward. Don't
delete that function — existing databases depend on it.

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

Venv lives outside the project so OneDrive never syncs it. Once per machine:

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements-dev.txt
```

```
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py    # desktop window
C:\Dev\venvs\ProfitDjinn\Scripts\python run.py        # dev server, http://localhost:5000
C:\Dev\venvs\ProfitDjinn\Scripts\python -m pytest     # 33 tests, must be green before pushing
```

## Where the data lives — read before touching paths

`paths.py` at the project root is the single source of truth. Two modes:

| | Frozen EXE | From source |
| --- | --- | --- |
| data dir | `%LOCALAPPDATA%\ProfitDjinn` | `<project>\instance` |
| holds | `app.db`, `.secret_key`, `webview\`, backups | same |

**`paths.py` is deliberately at the project root, not in `app/`.** `run_gui.py` must know
these paths *before* it imports the app package, because `app/config.py` reads
`SECRET_KEY` and `DATABASE_URI` out of `os.environ` while its class bodies are being
evaluated at import time. Importing `app.paths` would trigger `app/__init__.py` and lose
that race. Keep `paths.py` free of app imports, and keep the environment assignments at
the top of `run_gui.py` above the `from app import create_app` line.

`.secret_key` must stay stable. Flask-Login signs the "remember me" cookie with it, so
regenerating it silently signs Jon out of every session.

## The desktop shell

`run_gui.py` uses **pywebview**, which hosts the Flask app in an Edge WebView2 window.
There is no Chrome process and no browser chrome. WebView2 ships with Windows 10/11, so
there is nothing for a user to install.

Two settings carry the whole "stay signed in" feature; do not change them casually:

- `webview.start(private_mode=False, storage_path=...)` in `run_gui.py`. `private_mode`
  **defaults to `True`**, which discards cookies when the window closes.
- `REMEMBER_COOKIE_DURATION` on `GUIConfig` in `app/config.py`, set to 3650 days.
  `SESSION_COOKIE_SECURE` and `REMEMBER_COOKIE_SECURE` must stay `False` there — the
  window talks plain HTTP to `127.0.0.1`, so a Secure cookie would never come back.

### The bug this replaced

The old shell was `flaskwebgui`, which launched Chrome with
`--user-data-dir=<temp>/flaskwebgui<random-uuid>` and `rmtree`d it on exit. The remember
cookie was written correctly every time and then deleted, so sign-in never persisted and
Chrome's password manager started empty on every launch. `tests/test_stays_signed_in.py`
guards the server half of the fix. The browser half can only be verified by hand.

## Accounts and first start

No user is seeded. `_seed_database()` creates permissions, roles and settings (guarded
on `Role`, not `User`, so a fresh install with no account yet is not re-seeded on the
next start). `auth.require_setup_and_safe_password` (a `before_app_request` hook) sends
every request to `/auth/setup` until a user exists, then caches that in
`app.config["SETUP_COMPLETE"]`. Setup creates one `is_admin` user on the Administrator
role and signs them in; it redirects to login once any user exists.

Builds before 1.0.0-beta seeded `admin` / `LEGACY_DEFAULT_PASSWORD` (in
`app/blueprints/auth.py`, public in git history). Signing in with that password sets
`session["must_change_password"]` and the same hook pins the user to `/profile` until
either password-change path (profile form or `/api/change-password`) clears it. Both
paths refuse new == current. `tests/test_first_start.py` covers all of this, each test
on its own database via `create_app(config, overrides)`.

Version: `app/version.py`, injected as `app_version`, shown in the footer and on the
login and setup pages. Bump it per release; tag `v<version>`.

## Architecture

Application factory in `app/__init__.py`, four config classes in `app/config.py`
(`development` / `production` / `testing` / `gui`), extensions in `app/extensions.py`.

Blueprints: `auth`, `main`, `admin`, `database_mgr`, `customers`, `invoices`, `items`,
`work_orders`. Every route is login-gated; most are permission-gated with
`@permission_required('...')` from `app/utils/decorators.py`.

`work_orders_bp` is exempted from Flask-Limiter — rapid line entry blows past the
global 50/hour default. It is still login- and permission-gated. That default
(`["200 per day", "50 per hour"]` in `app/extensions.py`) applies to **every** route, not
just auth; exempt any future rapid-entry feature the same way.

**SQLite foreign keys are not enforced.** Nothing sets `PRAGMA foreign_keys=ON`, so
`ondelete="SET NULL"` on a column is documentation only. The SQLAlchemy relationship is
the only thing that touches a child row on parent delete; any other cross-table cleanup
has to be written in the route.

### Invoices and credit

- `_next_invoice_number()` takes the highest existing `invoice_number` with the prefix
  and adds one. The `invoice_next_number` setting is only a seed for an empty table and
  is never written back.
- Account credit is applied from the record-payment dialog with method
  `account_credit`. It raises `Invoice.credit_applied` (capped at available credit and
  balance due) rather than creating a `Payment`, so `Customer.account_credit` draws
  down. `net_total` = total minus `credit_applied`, and every paid/balance figure uses it.
- `mark_unpaid` deletes **all** payment records on the invoice.

### Work orders: why, and what must not regress

Built 2026-07-24 for one reason Jon stated: he stops at a customer several times over
weeks and forgets to bill the work. Every choice serves "never lose billable work."

- One rolling tab per customer that never closes (`work_orders.customer_id` UNIQUE,
  auto-created on first view). The optional per-line `project_label` keeps it
  navigable; unlabeled lines group under "General", sorted last.
- `pending -> completed -> billed` in one list; pending lines are the punch list.
- Billing is a preview screen Jon drives: pick lines, pick a roll-up (one line / by type
  / every line), hand-edit the invoice lines. The roll-up is computed client-side; the
  server consumes the final `line_items_json` and re-validates the selected line ids.
  Unchecked work stays on the tab.
- `WorkOrderLine.amount` is always recomputed server-side as `quantity * rate`; a posted
  amount is never trusted. Billed lines reject edit and delete.
- He asked for a no-charge flag and a private internal note per line, and **declined**
  priority flags and due dates. Don't add them back uninvited.
- `default_hourly_rate` ships as `0.00` on purpose: a guessed rate would silently produce
  wrong invoices.
- **Load-bearing:** `invoices.delete()` resets that invoice's work order lines to
  `completed` instead of orphaning them. `WorkOrderLine.effective_status` is the second
  net for lines whose invoice vanished any other way.

### Settings, not constants

Anything a user might want to change lives in the `settings` table and is read with
`get_setting()` (`app/utils/settings.py`), which is injected into every template.
Company name and address on invoices, invoice/work-order number prefixes and next
number, theme, font scale, login layout, hourly rate default — all settings. Don't
hardcode any of them. The desktop window title comes from `app_name` for the same reason.

Three bootstrap functions run inside the app context on every start and are all
idempotent: `_ensure_invoice_settings()`, `_ensure_permissions()`,
`_apply_brand_defaults()`. Add new settings and permissions there so existing
databases pick them up.

### Migrations — read this before changing a model

`migrations/` is **empty**. Flask-Migrate is installed but `flask db init` was never run.
Schema changes are handled two ways instead:

1. `db.create_all()` on startup creates any missing table.
2. `_run_migrations()` in `app/__init__.py` hand-writes `ALTER TABLE` for new columns
   on existing tables, guarded by an inspector check.

`create_all()` will not add a column to a table that already exists. If you add a
column to a model, you must also add a guarded `ALTER TABLE` to `_run_migrations()`
or it will silently fail against the live database.

### The login page is duplicated

`app/templates/auth/login.html` contains the form twice — once for the `left` layout and
once for `top`, chosen by the `login_logo_layout` setting. Both copies share the same
element ids because only one renders at a time. **Any change to the login form has to be
made in both blocks.**

## Building the EXE

```
build.bat
```

Output goes to `C:\Dev\ProfitDjinn\dist\ProfitDjinn\`, outside OneDrive, because it is
50+ MB of regenerable files. The EXE is standalone — the database is in
`%LOCALAPPDATA%`, so the build folder can be deleted and rebuilt at will.

`profitdjinn.spec` notes:

- `hiddenimports` lists every blueprint, model, and form module by name. **A new one
  must be added there** or the frozen EXE breaks while the dev server works. The Items
  feature shipped without this and was backfilled 2026-07-24.

- pywebview reaches WebView2 through .NET, so `webview`, `webview.platforms.winforms`,
  `webview.platforms.edgechromium`, `clr`, `clr_loader`, and `pythonnet` are all explicit
  hidden imports. PyInstaller finds none of them on its own: the platform backend is
  chosen at runtime.
- `collect_data_files("webview")` is required for `webview/js/` and the WebView2 interop
  DLLs in `webview/lib/`. Without it the window opens blank.
- **UPX is off on purpose.** It can corrupt .NET assemblies, and the failure shows up
  when the window opens rather than when the build runs.

## Known gaps

- `migrations/` empty (above).
- The invoice PDF has only been checked by eye, not asserted against a fixture.
- `scripts/import_old_data.py` reads `Old-Database-TV2/`, which is gitignored, so the
  importer is not runnable from a fresh clone.
- No EXE file-version resource; the version is visible in the UI only.
