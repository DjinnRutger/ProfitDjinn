# ProfitDjinn

Jon's invoicing and customer management app. **2.0** is a native Windows app (.NET 8 WPF),
shipped as one self-contained `ProfitDjinn.exe`. **1.x** was Flask + SQLAlchemy in an Edge
WebView2 window; it now lives in `legacy/` and stays runnable as the rollback path. Both
open the same database, `%LOCALAPPDATA%\ProfitDjinn\app.db`.

Private notes (repo history, personal setup) are in the gitignored `CLAUDE.local.md`.
This file is public: technical project facts only.

**Name check:** folder `ProfitDjinn`, repo `DjinnRutger/ProfitDjinn`, exe and window
`ProfitDjinn` (the `app_name` setting), README `ProfitDjinn`. All agree. It grew out of a
scaffold called **LocalVibe**; that name survives only in the brand-default migration
(`Seed.ApplyBrandDefaults`, and 1.x `_apply_brand_defaults()`), which moves old LocalVibe
setting values forward. Keep it: existing databases depend on it.

## 2.0 layout

- `src/ProfitDjinn.Core`: data, business rules, services, invoice PDF. No WPF, all tested.
  - `Data/`: `Database` (connection per call, pooling off, **foreign keys off**), `Schema`
    (the SQLAlchemy DDL, byte for byte, plus 1.x's two hand migrations), `Seed`, `Loader`.
  - `Model/`: entities with the 1.x computed properties (`Invoice.Total`, `BalanceDue`...).
  - `Rules/`: `PyMath` (Python/JavaScript number semantics), `Numbering`, `InvoiceRows`
    (the line builder's text-to-number rules), `Rollup` (bill-screen roll-ups).
  - `Services/`: one per area (Customers, Invoices, Items, WorkOrders, Billing, Reports,
    Backup, Settings, AppPassword). Refusals throw `UserFacingException` (or
    `ValidationException` with field names); successes return a `Notice` to show.
  - `Pdf/InvoicePdf.cs`: PDFsharp port of 1.x's fpdf2 layout, coordinate for coordinate.
- `src/ProfitDjinn.App`: the WPF app.
  - `Shell/MainWindow`: sidebar, top bar, breadcrumb, notices, dialogs, Back/Forward history,
    lock screen. `Routes.cs` lists every screen; pages never construct each other.
  - `Pages/`: one class per screen, built in code from `Infrastructure/Ui.cs` helpers.
  - `Controls/`: `Icon` (Bootstrap Icons as path data), `Badge`, `Card`, `StatTile`, `Field`,
    `Table` (Bootstrap table look), `LineBuilder`, `SuggestBox`, `BarChart`, `DoughnutChart`.
  - `Themes/`: `Theme.Light/Dark/Terminal.xaml` (same keys each) and `Controls.xaml`.
    `ThemeManager` swaps them and derives the brand brushes from `primary_color`.
- `tests/ProfitDjinn.Tests`: xUnit, 137 tests including the parity tests.
- `docs/port-spec.md` (behaviour) and `docs/design-spec.md` (look). "Fixed in 2.0" in the
  port spec lists every 1.x bug deliberately not copied. Read them before changing a screen.

## Build, test, run

```
.\build.ps1                       # tests, then publish <DevRoot>\publish\ProfitDjinn.exe
dotnet test tests\ProfitDjinn.Tests
dotnet run --project src\ProfitDjinn.App
```

`DevRoot` is `artifacts\` unless the gitignored `Directory.Build.local.props` moves it
(`C:\Dev\ProfitDjinn\dotnet\` on Jon's machines). Version: `<Version>` in
`Directory.Build.props`, shown in the footer.

**Never point a test run at the real database.** `PROFITDJINN_DATA_DIR` sends the app to
another data folder and puts `[test data: ...]` in the window title. .NET ignores a changed
`LOCALAPPDATA` (it asks Windows for the known folder), so that variable is no protection:
that is how a screenshot run once opened the real file (2026-10-02; it only added two empty
settings rows). `--page <name>` or `--page name:id` opens a screen at start.

Tools (`tools/Smoke/`), all on throwaway data folders:
- `Smoke-Flow.ps1 -Exe <exe>`: UI Automation run through create customer, log work, bill,
  record payment, a recurring invoice that back-fills three and one issued early, editing it,
  service dates, the PDF preview and switching work orders off, then checks the database. Inputs are found by their field label
  (`Field` sets the accessible name) or placeholder.
- `Capture.ps1` / `Shoot-Themes.ps1`: screenshots of a page in each theme on fixture copies.

**Test runs must not take over the desktop** (Jon, 2026-10-05). The smoke scripts and
`Capture.ps1` set `PROFITDJINN_OFFSCREEN=1` (honoured only with `PROFITDJINN_DATA_DIR`): the
window opens off-screen with `ShowActivated=false`, and whenever UI Automation brings it to the
front anyway, `App.GiveBackForeground` hands the foreground straight back to the user's window.
A run takes focus for well under a second in total. Consequences:
- Drive the app through UI Automation only: no SendKeys, no mouse, no `SetFocus`. Dropdowns are
  `Controls/Dropdown` (a ComboBox whose ValuePattern sets the choice by label; an opened list
  closes as soon as the window loses focus); table rows that open something are invokable
  `DataItem`s (`Invoke-Row`). Use `new Dropdown`, never `new ComboBox`, in new screens.
- The real-keystroke picker fill-in checks run only with `-Keys` (window on screen, takes focus
  for a few seconds); run them before a release, after telling Jon.
- Windows does not draw an off-screen window: `Capture.ps1` has the app draw itself to a PNG
  (`PROFITDJINN_SNAPSHOT`), and failure screenshots move the window on screen behind everything.

## Parity with 1.x is tested, not assumed

- `tools/Parity/make_fixture.py` runs `Fixtures/parity_ops.json` through the real 1.x routes
  (`legacy/`); `ParityTests` replays the same ops through 2.0 and compares every row and every
  computed figure bit for bit. `KnownFixes` in that test pins each deliberate difference.
- `tools/Parity/rollup_reference.mjs` runs 1.x's own bill-screen JavaScript (lifted out of
  `legacy/app/templates/work_orders/bill.html`) for `RollupTests`.
- `tools/Parity/dump_figures.py` does the figure comparison on a copy of a real database
  (see `CLAUDE.local.md`). Regenerate fixtures after changing a 1.x rule or adding a case.
- **The number rules matter.** Python 3.12+ `sum()` is Neumaier-compensated, so totals use
  `PyMath.Sum`, never LINQ `Sum`. `round()` is half-to-even on the exact binary value
  (`PyMath.Round`); JavaScript `toFixed` is half-up (`PyMath.JsToFixed`). The line builders
  use plain JavaScript addition (`PyMath.JsSum`). 1.x templates printed money with `%.2f`,
  no thousands separator (`Ui.Money`); the line builders and the PDF group thousands.
- **Database compatibility.** 2.0 keeps foreign keys off, writes dates as `YYYY-MM-DD` text,
  and only adds settings rows (`app_password_hash`, `theme`). 1.x must keep opening the
  file: tested both ways. Cascades are done in code (customer delete, invoice delete
  returning work lines to the tab).
- SQLite tables use INTEGER PRIMARY KEY without AUTOINCREMENT, so deleting the highest row
  frees its id. Fixture ops that reference ids must account for it.

## 2.0 gotchas

- Compiled WPF XAML does not support `x:Boolean`, and a named transform inside a template is not
  a trigger target. Inside `MainWindow`, `Icon` means the window's icon property: write
  `Controls.Icon`.
- `TextBox` applies `Padding` to its content host itself; the template must not add it again.
- `Table` treats a fixed column width as a minimum, like HTML; columns under 60px get 8px
  side padding so checkboxes and icons are not clipped.
- Shadows sit on a separate layer behind cards so the card text keeps ClearType.
- A page whose constructor throws `UserFacingException` (nothing to bill, deleted record)
  is not added to history; the shell shows the message instead.
- `Store` saves an upgrade backup (`Services/UpgradeBackup.cs`) before `Schema.Ensure` whenever
  `db_app_version` differs from the running version. Keep it ahead of every write. A new column
  still needs its own guarded `ALTER TABLE` in `Schema.RunMigrations`; `create table if not
  exists` will not add it.
- Uploaded logo and sidebar icon live in `%LOCALAPPDATA%\ProfitDjinn\branding\`. A
  non-empty `login_logo` / `app_icon_img` setting with no file shows the built-in genie.

## Expenses (2.2, optional)

Off by default (`expenses_enabled`). No 1.x equivalent and no 1.x table touched: six new
tables (`vendors`, `expense_categories`, `expenses`, `expense_payments`, `expense_receipts`,
`recurring_expenses`) that 1.x ignores. Phase 2 added the Profit & Loss page (below).

- **Gating:** `MainWindow.ExpenseNav` items hide in `RefreshChrome`; every expense route in
  `Routes.cs` is wrapped in `Exp(...)`, which opens the Dashboard while it is off. New expense
  screens must go through `Exp` too.
- **Status comes from payments only.** No paid flag, so a report can count by expense date
  (accrual) or payment date (cash). Payments cannot exceed the balance; no vendor credit.
- **Categories** are seeded once, when `expense_categories` is first created (checked in
  `Schema.Ensure` before the DDL), so deleted starters never come back. In-use ones can only
  be hidden.
- **Recurring** (`RecurringService`): dates come from the start month (day 31 -> last day of
  short months). `generated_through` means nothing on or before it is created again, so a
  deleted occurrence stays deleted and edits never back-fill; resuming skips the paused gap.
  `GenerateDue` runs once per start in the app (`MainWindow.RunRecurringOnce`), after saving
  a template, and when Expenses is switched on; never in the `Store` constructor.
- **Receipts** (`ReceiptStore`): copied to `<folder>\yyyy\E{id}-{name}`; rows keep the path
  relative to the folder plus the folder used at save time. `Resolve` tries the current
  folder, then that saved one, so "Leave Them" after a folder change still works. Not in the
  .db backup; the Backup page says so.
- **Profit & Loss** (`ProfitService`, `Pages/ProfitLossPage.cs`, `Pdf/ProfitPdf.cs`): every
  figure is a sum of dated entries from `ProfitService.Entries(basis)`, so months, years, CSV
  and PDF always agree. Cash = payments received and made on their dates; an invoice marked
  paid with no payment rows (older data has these) counts its net total on `paid_date`.
  Accrual = invoice total and expense amount on their own dates. Applied account credit is
  never income twice. This is not the Revenue page's figure, which counts AmountPaid by
  invoice date (the 1.x rule, kept for parity). The dashboard's Net Profit tile is cash basis
  and, with Expenses on, the tiles go to two rows of three.
- `tools/Smoke/Smoke-Expenses.ps1` drives the whole feature through UI Automation.

## Settings and updates (2.3)

- **Settings page** (`Pages/SettingsPage.cs`): sections listed on the left, one shown at a
  time, plain labels instead of setting keys. Every editable value registers a reader in
  `_readers`; the snapshot `_initial` decides `Dirty`. A new setting must be placed in a
  section (or it appears under "Advanced" by key) and its widget must call `Changed()`.
  Switches use Checked/Unchecked, never Click: UI Automation and the keyboard do not raise
  Click. Uploads and the app password still act at once.
- **Pinned bar:** `AppPage.PinnedBar` is shown by the shell above the footer, outside the
  scroll area. Settings uses it for "unsaved changes"; `CanLeaveAsync` asks before leaving.
- **`Ui.Stack` overwrites each child's top margin with the gap.** Put spacing in a wrapper's
  padding, not in the child's margin, or it disappears.
- **Update check** (`Services/UpdateService.cs`): GitHub API `releases/latest` for the public
  repo, once a day at start, only while `update_check_enabled` is on. The answer is stored in
  settings; `MainWindow.ShowUpdateBadge` draws the footer badge from it, and only github.com
  links are opened. "Latest" skips pre-releases, so **publish new versions as full releases**
  with the asset named `ProfitDjinn.exe`, or the badge and the README download link never
  move on.

## Recurring invoices (2.4)

No on/off switch: nothing shows until a schedule exists, except the dashboard's first tile
(Upcoming in 30 days, which replaced the Customers count). Three new tables
(`recurring_invoices`, `recurring_invoice_lines`, `recurring_invoice_runs`); no 1.x table touched.

- **`RecurringInvoiceService.Issue`/`IssueIn` is the only place a schedule becomes an
  invoice.** Generation at start, Issue Now, early Print/PDF all use it. Keep it that way: it
  is the Stripe hook (`docs/stripe-readiness.md`).
- **Never twice:** `generated_through` (as in recurring expenses) plus the runs table's unique
  `(recurring_id, date)`. A deleted invoice keeps its run row (link cleared in
  `InvoiceService.Delete`), so its date is not recreated. Issuing early keeps the scheduled
  date and only the next date can be issued or skipped (`RequireNext`).
- **Dates** come from `Rules/Schedule.cs`, shared with `RecurringService`; change the rule
  there and both features follow (the expense recurring tests guard it).
- `{month}`/`{year}` are filled by `Rules/PeriodText` at issue time (lines and notes); the
  schedule stores them raw. Lists show them filled for the next date.
- Generation runs in `MainWindow.RunRecurringOnce` (invoices, then expenses; one reload, then
  both notices) and after saving a schedule. Never in the `Store` constructor.
- `NewDraft` starts on the 1st of next month on purpose: a first date of today would create an
  invoice the moment the form is saved.
- Customer delete removes its schedules (`RecurringInvoiceService.DeleteSchedules`).
- `Ui.IconButton` sets the accessible name to its tooltip and `Link` is a UI Automation
  hyperlink: `Smoke-Flow.ps1` clicks both ("Smoke Test Co", "View the next invoice").

## 2.5: features, service dates, PDF preview

- **Settings > Features** holds the on/off switches (`workorders_enabled`, `expenses_enabled`).
  Work order routes go through `Routes.Wo(...)` like expense routes go through `Exp(...)`;
  anything new that shows work orders must check `Store.WorkOrders.Enabled`.
- **Business settings default to empty** and show hints; `Seed.ClearOldSamples` clears the old
  sample values at start. Never seed sample text into a real setting again: use a placeholder.
- **Service dates** live in `invoice_line_service`, never on `invoice_lines` (a 1.x table;
  `ParityTests` checks its columns). Every path that deletes invoice lines must use
  `InvoiceService.DeleteLines`. `InvoiceLineDraft`/`InvoiceRowInput` carry the dates;
  `LineBuilder(..., LineDates.Dates | Period)` edits them.
- **PDF preview:** `PdfPreviewWindow.Show(owner, bytes, title, save, print)`. `InvoiceOutput`
  has byte-level `SavePdf`/`Print` for any PDF.
- **Layout helpers:** `Ui.Row` spaces children added later too (`GapRow`); `Ui.Empty` returns
  a padded Border (do not cast it to StackPanel); `Grid.EqualHeight()` makes side-by-side cards
  match. Don't write `existing?.X ?? draft!.X` in a form: it reaches the null draft whenever a
  saved value is null (that crashed Edit Recurring Invoice in 2.4.0).

## 2.6: type-to-pick customers and vendors

- `Controls/RecordPicker.cs` replaces the customer and vendor ComboBoxes (invoice, recurring
  invoice, expense, recurring expense forms): type to filter, the rest of the best name fills
  in (selected; Tab/Enter accept), an exact name picks the record, and unknown text shows an
  "Add Customer"/"Add Vendor" button. `SelectedId`, `HasUnmatchedText`, `SelectionChanged`.
- `Pages/QuickAdd.cs` builds the pickers and the add dialogs (`shell.OpenDialog`). It must never
  `Reload`: that rebuilds the form from the database and loses what was typed.
- The suggestion list is a Popup window, so a process's `MainWindowHandle` can briefly be the
  popup (no title) when a picker has focus at start; wait for the titled window.
- Smoke tests type into pickers with `Type-Into` (exact names) and use real keystrokes
  (`SendKeys`, window brought to the front) for the fill-in.

## 2.6: bookkeeping feedback (expenses, banking)

Jon's rule for these: never disturb invoicing, keep it simple. Everything is either inside
Expenses or behind **Bank accounts** (Settings > Features, `banking_enabled`, off by default).

- **Cost of revenue:** `expense_categories.cost_of_revenue` (Categories page "Counts As").
  `ProfitReport.ShowGross` is true only once a category is flagged; only then do the P&L page,
  PDF and CSV show cost of revenue, gross profit and operating expenses.
- **MRR/ARR:** `RecurringInvoiceService.Revenue()` (read only), tiles on Recurring Invoices.
- **Who paid:** `expense_payments.paid_from` (business / owner / noncash), `account_id`,
  `reimbursed_on`. `ExpenseService.InsertPayment` is the only writer; owner payments never
  carry an account. Mark Paid Back can record the payback from an account.
- **Mileage:** `expense_mileage` (miles, rate at the time); amount = miles x rate, paid with a
  no-cash payment. `ExpenseDraft.Miles`. The rate is the `mileage_rate` setting.
- **Banking:** `BankService`, tables `bank_accounts`, `bank_transactions`, `payment_accounts`.
  - The P&L never reads bank tables, so contributions, draws and transfers are never revenue.
  - A payment recorded with an account creates one linked row (invoice payments through the
    `payment_accounts` side table: `payments` is a 1.x table). Every path that deletes payments
    calls `BankService.UnlinkInvoicePayments` / `UnlinkExpensePayments` **before** the delete.
  - `InvoiceService.RecordPayment(..., depositedTo: null)` writes exactly what 2.5 wrote
    (BankTests checks it); the dialog only shows "Deposited To" with Bank Accounts on.
  - Transfers are two rows sharing `transfer_id`; a processor payout is a transfer.
    `Bridge` shows what a payout pays out; differences are shown, never fixed.
  - Reconcile finishes only at a zero difference; reconciled rows are locked.
  - SQLite returns an integer 0 for an empty or all-integer SUM: cast to REAL when Dapper maps it
    to a double (that crashed the account list once).
- Smoke: `tools/Smoke/Smoke-Banking.ps1`; Smoke-Expenses covers mileage, owner-paid, COGS.

## 2.7: Revenue/Items switches, P&L drill-down

- `revenue_enabled` / `items_enabled` (on by default) join Settings > Features. Routes go
  through `Routes.Rev(...)` / `Itm(...)`; `ReportService.RevenueEnabled`, `ItemService.Enabled`.
  The dashboard Revenue tile stays (not clickable while off); the invoice quick-add checks
  `Store.Items.Enabled`.
- `ProfitService.Detail(basis, part, scope)` backs `ProfitDetailPage`. **The headline figure is
  read from `Report()`** (tile field, `ProfitMonth`, category/vendor/year row), never re-summed:
  Operating, Gross and Net are rounded differences there. The entry lists are the breakdown;
  `ProfitDetailTests` checks every figure matches the page and the lists reconcile to it.
  `Entry` carries `InvoiceId`/`ExpenseId` (trailing defaulted parameters).
- `Ui.DrillText` is a `Link` in the table's own colour (`Link.SetBrushes`): underline on hover,
  a UI Automation hyperlink. `PairBarChart.Clicked` / `DoughnutChart.Clicked` give the index.
- Smoke: Smoke-Flow switches Revenue and Items off; Smoke-Expenses drills into the net total.
  PowerShell 5.1 reads the .ps1 files as ANSI: match text containing "·" with `Wait-Like`.

## 1.x reference (Flask, in legacy/)

Everything below describes the 1.x code in `legacy/`. It stays accurate for that code.

### What it does

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

### Run it

Venv lives outside the project so OneDrive never syncs it. Once per machine:

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements-dev.txt
```

From `legacy/` (`pip install -r legacy\requirements-dev.txt` the first time):

```
cd legacy
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py    # desktop window
C:\Dev\venvs\ProfitDjinn\Scripts\python run.py        # dev server, http://localhost:5000
C:\Dev\venvs\ProfitDjinn\Scripts\python -m pytest     # 33 tests, must be green before pushing
```

### Where the data lives — read before touching paths

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

### The desktop shell

`run_gui.py` uses **pywebview**, which hosts the Flask app in an Edge WebView2 window.
There is no Chrome process and no browser chrome. WebView2 ships with Windows 10/11, so
there is nothing for a user to install.

Two settings carry the whole "stay signed in" feature; do not change them casually:

- `webview.start(private_mode=False, storage_path=...)` in `run_gui.py`. `private_mode`
  **defaults to `True`**, which discards cookies when the window closes.
- `REMEMBER_COOKIE_DURATION` on `GUIConfig` in `app/config.py`, set to 3650 days.
  `SESSION_COOKIE_SECURE` and `REMEMBER_COOKIE_SECURE` must stay `False` there — the
  window talks plain HTTP to `127.0.0.1`, so a Secure cookie would never come back.

#### The bug this replaced

The old shell was `flaskwebgui`, which launched Chrome with
`--user-data-dir=<temp>/flaskwebgui<random-uuid>` and `rmtree`d it on exit. The remember
cookie was written correctly every time and then deleted, so sign-in never persisted and
Chrome's password manager started empty on every launch. `tests/test_stays_signed_in.py`
guards the server half of the fix. The browser half can only be verified by hand.

### Accounts and first start

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

### Architecture

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

#### Invoices and credit

- `_next_invoice_number()` takes the highest existing `invoice_number` with the prefix
  and adds one. The `invoice_next_number` setting is only a seed for an empty table and
  is never written back.
- Account credit is applied from the record-payment dialog with method
  `account_credit`. It raises `Invoice.credit_applied` (capped at available credit and
  balance due) rather than creating a `Payment`, so `Customer.account_credit` draws
  down. `net_total` = total minus `credit_applied`, and every paid/balance figure uses it.
- `mark_unpaid` deletes **all** payment records on the invoice.

#### Work orders: why, and what must not regress

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

#### Settings, not constants

Anything a user might want to change lives in the `settings` table and is read with
`get_setting()` (`app/utils/settings.py`), which is injected into every template.
Company name and address on invoices, invoice/work-order number prefixes and next
number, theme, font scale, login layout, hourly rate default — all settings. Don't
hardcode any of them. The desktop window title comes from `app_name` for the same reason.

Three bootstrap functions run inside the app context on every start and are all
idempotent: `_ensure_invoice_settings()`, `_ensure_permissions()`,
`_apply_brand_defaults()`. Add new settings and permissions there so existing
databases pick them up.

#### Migrations — read this before changing a model

`migrations/` is **empty**. Flask-Migrate is installed but `flask db init` was never run.
Schema changes are handled two ways instead:

1. `db.create_all()` on startup creates any missing table.
2. `_run_migrations()` in `app/__init__.py` hand-writes `ALTER TABLE` for new columns
   on existing tables, guarded by an inspector check.

`create_all()` will not add a column to a table that already exists. If you add a
column to a model, you must also add a guarded `ALTER TABLE` to `_run_migrations()`
or it will silently fail against the live database.

#### The login page is duplicated

`app/templates/auth/login.html` contains the form twice — once for the `left` layout and
once for `top`, chosen by the `login_logo_layout` setting. Both copies share the same
element ids because only one renders at a time. **Any change to the login form has to be
made in both blocks.**

### Building the EXE

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

### Known gaps

- `migrations/` empty (above).
- The invoice PDF has only been checked by eye, not asserted against a fixture.
- `scripts/import_old_data.py` reads `Old-Database-TV2/`, which is gitignored, so the
  importer is not runnable from a fresh clone.
- No EXE file-version resource; the version is visible in the UI only.
