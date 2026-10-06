# ProfitDjinn 2.0 port spec: behaviour

The Flask app (1.0.0-beta) is the reference. This file records what the WPF port must do,
taken from a read of the Python source on 2026-10-02. File references are to the Python
code under `app/` (moved to `legacy/app/` once 2.0 ships). The visual spec is
`design-spec.md`.

**Rule of thumb:** money math, statuses and cascades are copied exactly. Bugs listed under
"Fixed in 2.0" are deliberately not copied.

## Traps for a port

1. **Foreign keys are not enforced.** The live DB has `PRAGMA foreign_keys = 0`.
   Microsoft.Data.Sqlite does not turn them on unless asked, so never send
   `PRAGMA foreign_keys = ON`. Cascades are done in code (see "Cascades").
2. **Defaults live in Python, not in the DB.** Only `users.theme` (`'light'`) and
   `invoices.credit_applied` (`0`) have SQL defaults. Every insert must supply every NOT
   NULL column.
3. **Old databases have `users.dark_mode BOOLEAN NULL`**, which no model declares. Leave it.
4. **Storage formats:** booleans `0`/`1`; `Date` is text `YYYY-MM-DD`; `DateTime` is naive
   UTC text `YYYY-MM-DD HH:MM:SS.ffffff`; money is REAL. No `alembic_version` table,
   `user_version` 0.
5. Settings that do nothing anywhere: `items_per_page`, `session_timeout`,
   `maintenance_mode`, `allow_registration`, `default_theme`. Keep the rows; ignore them.

## Schema

`[x]` = the default Python supplies on insert. "uidx" = `CREATE UNIQUE INDEX ix_<table>_<col>`.

| Table | Columns | Keys |
|---|---|---|
| permissions | id PK; name VARCHAR(128) NOT NULL; description VARCHAR(255) | UNIQUE(name) |
| roles | id PK; name VARCHAR(64) NOT NULL; description VARCHAR(255); created_at DATETIME [utcnow] | UNIQUE(name) |
| role_permissions | role_id INT NOT NULL FK roles; permission_id INT NOT NULL FK permissions | PK(role_id, permission_id) |
| users | id PK; username VARCHAR(64) NOT NULL; email VARCHAR(120) NOT NULL; password_hash VARCHAR(512) NOT NULL; is_active BOOL NOT NULL [1]; is_admin BOOL NOT NULL [0]; role_id INT FK roles; created_at DATETIME; last_login DATETIME; theme VARCHAR(32) NOT NULL DEFAULT 'light' | uidx username, email |
| settings | id PK; "key" VARCHAR(128) NOT NULL; value TEXT; type VARCHAR(32) NOT NULL ['text']; description VARCHAR(255); category VARCHAR(64) ['general']; options TEXT (JSON array) | uidx key; idx category |
| audit_logs | id PK; user_id INT FK users; action VARCHAR(64) NOT NULL; resource VARCHAR(64); resource_id INT; details TEXT; ip_address VARCHAR(45); created_at DATETIME | idx created_at |
| customers | id PK; name VARCHAR(200) NOT NULL; attn, address(300), city(100), state(50), zip_code(20), phone(50), email(200) all ['']; notes TEXT ['']; is_active BOOL NOT NULL [1]; created_at DATETIME | idx name |
| invoices | id PK; invoice_number VARCHAR(50) NOT NULL; customer_id INT NOT NULL FK; date DATE NOT NULL [today]; notes TEXT ['']; term1, term2 VARCHAR(300) ['']; paid BOOL NOT NULL [0]; paid_date DATE; created_at DATETIME; credit_applied FLOAT NOT NULL DEFAULT 0 | uidx invoice_number |
| invoice_lines | id PK; invoice_id INT NOT NULL FK; description VARCHAR(500) NOT NULL; quantity FLOAT NOT NULL [1.0]; amount FLOAT NOT NULL | order by id |
| payments | id PK; invoice_id INT NOT NULL FK; customer_id INT NOT NULL FK; amount FLOAT NOT NULL; method VARCHAR(30) NOT NULL ['cash']; check_number VARCHAR(50) ['']; date DATE NOT NULL [today]; notes TEXT ['']; created_at DATETIME | order by date |
| service_items | id PK; description VARCHAR(500) NOT NULL; price FLOAT NOT NULL [0.0]; is_active BOOL NOT NULL [1] | |
| work_orders | id PK; customer_id INT NOT NULL FK; number VARCHAR(50) NOT NULL; notes TEXT ['']; is_active BOOL NOT NULL [1]; created_at DATETIME | UNIQUE(customer_id); uidx number |
| work_order_lines | id PK; work_order_id INT NOT NULL FK; project_label VARCHAR(120) ['']; description VARCHAR(500) NOT NULL; line_type VARCHAR(20) NOT NULL ['labor']; status VARCHAR(20) NOT NULL ['pending']; date_performed DATE; quantity FLOAT NOT NULL [1.0]; rate FLOAT NOT NULL [0.0]; amount FLOAT NOT NULL [0.0]; no_charge BOOL NOT NULL [0]; internal_note TEXT ['']; invoice_id INT FK invoices (ON DELETE SET NULL, not enforced); billed_at DATE; created_at DATETIME | idx status; order by id |

Hand migrations (`app/__init__.py` `_run_migrations`), each guarded by a column check:
1. `ALTER TABLE users ADD COLUMN theme VARCHAR(32) NOT NULL DEFAULT 'light'`, then
   `UPDATE users SET theme='dark' WHERE dark_mode=1` if `dark_mode` exists.
2. `ALTER TABLE invoices ADD COLUMN credit_applied FLOAT NOT NULL DEFAULT 0`.

Startup order: create tables, then the migrations, then seed if no role exists, then
insert any missing invoice settings, permissions and brand defaults.

Payment methods: `cash` Cash, `check` Check, `credit_card` Credit Card, `ach` ACH,
`venmo` Venmo, `other` Other. An unknown method displays title-cased. `account_credit`
is a dialog option only and never stored.

### Settings seeded

`_seed_database` (only on an empty DB):

| key | default | type | category | options |
|---|---|---|---|---|
| app_name | ProfitDjinn | text | general | |
| app_tagline | Rub the Lamp, Send the Invoice, Count the Gold! | text | general | |
| app_icon | bi-lightning-charge-fill | text | appearance | |
| footer_text | ProfitDjinn — Built with Flask | text | general | |
| primary_color | #2563eb | color | appearance | |
| default_theme | light | select | appearance | ["light","dark","terminal"] |
| allow_registration | false | boolean | security | |
| maintenance_mode | false | boolean | general | |
| items_per_page | 20 | number | general | |
| session_timeout | 480 | number | security | |

`_ensure_invoice_settings` (insert if missing, every start):

| key | default | type | category | options |
|---|---|---|---|---|
| company_name | Your Name | text | invoices | |
| company_address | 123 Main Street | text | invoices | |
| company_city | Anytown | text | invoices | |
| company_state | ST | text | invoices | |
| company_zip | 00000 | text | invoices | |
| company_email | you@example.com | text | invoices | |
| company_phone | (555) 555-0100 | text | invoices | |
| invoice_prefix | INV | text | invoices | |
| invoice_next_number | 1001 | number | invoices | |
| invoice_term1 | Payment Terms: Due within 30 days | text | invoices | |
| invoice_term2 | Make all checks payable to Your Name | text | invoices | |
| ui_font_scale | 1.0 | select | ui | ["0.80","0.85","0.90","0.95","1.0","1.05","1.10","1.15","1.20","1.25"] |
| login_logo_layout | left | select | login | ["top","left"] |
| login_logo | login_logo.png | text | login | |
| app_icon_img | app_icon.png | text | appearance | |
| workorder_prefix | WO | text | workorders | |
| workorder_next_number | 1001 | number | workorders | |
| default_hourly_rate | 0.00 | number | workorders | |

Typed values: boolean is true for lowercased `true`/`1`/`yes`; number is int, else float,
else 0; json is parsed, else `{}`; anything else is the raw string.

Brand-default migration (every start): app_name `LocalVibe`→`ProfitDjinn`; app_tagline
`Your Local Network Hub`→the genie tagline; footer_text `LocalVibe — Built with Flask`→
`ProfitDjinn — Built with Flask`; login_logo_layout `top`→`left`. Each one changes only a
value that still equals the old default.

## Business rules

### Numbering (`invoices.py:21-36`; work orders identical at `work_orders.py:37-53`)

`prefix = invoice_prefix`. Find the highest existing number that starts with the prefix,
parse the suffix as an int, and add 1. If there is no match, or the suffix is not numeric,
use the `invoice_next_number` setting. Format `{prefix}{num:04d}`. The setting only seeds an
empty table and is never written back. The number is a suggestion: the user may edit it,
and it is stored `.strip().upper()`. It must be unique.

### Invoice totals (`models/invoice.py`)

- `total = Σ line.amount`. A line's `amount` is the extended total; `unit_price = amount/quantity`
  (or `amount` when quantity is 0).
- `net_total = max(0, total − credit_applied)`
- `amount_paid` = Σ payments if any exist; else `net_total` if `paid`; else 0
- `balance_due = max(0, net_total − amount_paid)`
- `credit_amount = max(0, amount_paid − net_total)` (overpayment)
- `is_partial = 0 < amount_paid < net_total`
- Status: `paid` gives Paid/success; else `is_partial` gives Partial/info; else Unpaid/warning.
- `_recalc_paid_status(inv, when=today)`: if `amount_paid >= net_total`, then `paid = True`
  and `paid_date = paid_date or when`; else `paid = False` and `paid_date = None`.

### Invoice create and edit (`invoices.py:113-287`)

- Customer, number and date are required. Lines must be a non-empty list.
- Create: `paid` from the checkbox; `paid_date = today` if paid. Each line stores
  `description.strip()`, `quantity` and `amount` (the extended total), unrounded.
- Edit:
  - If the invoice has work-order lines and the customer changed, refuse.
  - Paid flag: if `form.paid and not invoice.paid`, set `paid_date = today`; if not
    `form.paid`, set `paid_date = None`. Then `paid = form.paid`.
  - Delete all lines and recreate them.
  - `if credit_applied > total: credit_applied = round(total, 2)`.
  - Only when the box is unchecked, run `_recalc_paid_status`. This can turn paid back on
    when payments already cover the total.
- The invoice form's live total treats a blank or zero qty as 1: `Σ (qty||1) × (price||0)`.
  The posted amount is `qty × price`.

### Payments (`invoices.py:325-452`)

- Amount must be > 0 ("Payment amount must be greater than zero.").
- `account_credit` method:
  - `applied = round(min(amount, customer.account_credit, balance_due), 2)`.
  - If `applied <= 0`: "No account credit is available to apply."
  - `credit_applied = round(credit_applied + applied, 2)`, then `_recalc_paid_status`.
  - No Payment row is created. The form date is ignored.
- Other methods:
  - Create a Payment with the amount unrounded; `check_number` only for `check`; date from
    the form or today; notes trimmed.
  - If `Σ payments >= net_total`: `paid = True`, `paid_date = paid_date or payment date`.
    Otherwise `paid = False`, `paid_date = None`.
- Delete payment: if the remaining Σ ≥ net_total, `paid = True` (paid_date unchanged);
  otherwise `paid = False`, `paid_date = None`.
- Mark unpaid: delete **all** payments; `paid = False`; `paid_date = None`. Do **not** touch
  `credit_applied`.

### Invoice delete (`invoices.py:292-320`)

Every work-order line on the invoice gets `status = 'completed'`, `invoice_id = NULL`,
`billed_at = NULL`. Then delete the invoice with its lines and payments. Report how many
work lines went back to the tab.

### Customer totals (`models/customer.py:48-67`)

- `total_invoiced = Σ invoice.total` (gross)
- `total_outstanding = Σ balance_due`
- `total_paid = total_invoiced − total_outstanding`
- `account_credit = max(0, Σ credit_amount − Σ credit_applied)`
- Customer form save: trim everything, uppercase state, lowercase email. Email (only when
  given) must contain `@` with a `.` after it.

### Work order lines (`models/work_order.py`, `blueprints/work_orders.py`)

- One work order per customer, created on the first visit to the customer's tab.
- `status` is pending | completed | billed. `line_type` is labor | part | service | other
  (labels Labor, Parts, Services, Other; unknown shows Other).
- `amount = round((qty or 0) × (rate or 0), 2)`.
- `effective_status = 'completed'` if `status == 'billed'` and `invoice_id` is NULL;
  otherwise `status`.
- Status label: billed → Billed/success; completed → "No Charge"/info if no_charge, else
  "Ready to Bill"/warning; pending → Pending/secondary.
- `quantity_label`: `"{qty:g} h"` for labor, else `"{qty:g}"`.
- Buckets use effective status:
  - `ready_to_bill_total = Σ amount (completed, not no_charge)`
  - `billed_total = Σ amount (billed, not no_charge)`
  - `pending_count`
  - `has_open_work = any pending or completed`
- `grouped_open`: non-billed lines grouped by `project_label.strip() or "General"`, labelled
  groups in ordinal (case-sensitive) order, General last.
- `grouped_billed`: grouped by invoice_id, newest `(billed_at, invoice_id)` first. A group's
  total includes no-charge lines.
- `open_labels`: sorted distinct labels across all lines (for the Project suggestions).
- Field rules (`_apply_line_fields`): description `.strip()[:500]`; label `.strip()[:120]`;
  note stripped; an invalid type becomes labor; `quantity = max(0, parsed or 1.0)`;
  `rate = max(0, parsed or 0.0)`; `no_charge` is set by any non-empty value; then recompute
  the amount.
- Add a to-do (the default mode): description required; label only; pending; labor;
  `rate = default_hourly_rate`; qty 1; **amount 0**.
- Log work: all fields; completed; `date_performed` from the form or today.
- Edit: refused if billed. Apply the fields. If a date is given, set it and mark completed.
  Otherwise a pending line gets amount 0, and a completed line keeps its old date.
- Complete: apply the fields; completed; date from the form or today.
- Reopen: pending; date NULL; amount 0; quantity and rate kept.
- Toggle no-charge: flips the flag (allowed on pending lines too).
- Delete: refused if billed.

### Billing (`work_orders.py:384-545`, `templates/work_orders/bill.html`)

- Billable lines: effective status completed. An optional label filter matches
  `label_or_general`.
- Sort (`_sorted_billable`): General last, then label case-insensitive, then
  `date_performed` (none first), then id.
- Preselect: all completed lines that are not no-charge, unless specific ids are requested.
- Roll-up modes, built from the selected lines `sel`; `billable = sel` minus no-charge lines:
  - **detailed:** one row per selected line, **including no-charge lines**.
    - desc = `(date ? "Mon DD - " : "") + description + (noCharge ? " (no charge)" : "")`
    - qty = the line's quantity (1.x: `qty || 1`; see "Fixed in 2.0")
    - unit = `noCharge ? 0 : rate`
  - **type:** for each type in order labor, part, service, other, over `billable`:
    - amt = Σ amount
    - qty = Σ qty for labor, else 1; if 0, use 1; round to 2 decimals
    - unit = amt / qty
    - desc = `Labor` | `Parts & materials` | `Services` | `Other work`, plus dateRange
  - **one** (the default): a single row. unit = Σ amount of `billable`; qty 1. desc = the
    label when all of `sel` share exactly one label (which may be "General"), else
    `Service work`; plus dateRange(sel).
  - **dateRange:** only dated lines, sorted by ISO date. `" (Mon DD)"` when first equals
    last, else `" (Mon DD - Mon DD)"`. Days zero-padded ("Jul 04"), plain ASCII hyphen.
- **Rounding quirk (keep it):** the unit price is rounded to 2 decimals before the posted
  amount is computed as `qty × rounded unit`. Three hours totalling $100 bill as 3 × 33.33
  = 99.99, and the screen shows the mismatch alert.
- Mismatch alert: when `|invoice total − selected total| >= 0.01` and there is at least one
  row: "differs ... (+$x / −$x)".
- Hand-edited invoice rows are "dirty". Rebuilding from the selection asks first: "Rebuild
  the invoice lines from your selection? Your edits will be lost."
- Submit:
  - At least one selected line; every selected line still completed (otherwise warn and
    reload the selection).
  - Non-empty rows; unique invoice number; invoice created with `paid = False`.
  - Rows with an empty description are skipped. desc `[:500]`; qty as parsed;
    `amount = round(amount, 2)`.
  - Every selected line: `status = 'billed'`, `invoice_id`, `billed_at = today` (today, not
    the invoice date).

### Dashboard (`main.py:14-76`)

- Customers: count of active customers. (2.4 shows Upcoming invoices in this place instead;
  see "New in 2.4". `DashboardStats.ActiveCustomers` is still computed.)
- Total invoices: count.
- Outstanding: Σ balance_due over invoices with balance > 0, plus that count.
- Unbilled work: `Σ amount` over work-order lines with `invoice_id IS NULL AND status !=
  'pending' AND no_charge = 0`. The badge counts pending lines.
- {year} revenue: Σ amount_paid over invoices dated in the current year.
- Recent invoices: 5 newest by date, showing the gross total and the status.

### Revenue (`main.py:79-182`)

- Year buttons: "All Years" plus every year that has an invoice, newest first. The current
  year is always present, with a YTD badge. The default is the current year.
- Filter: invoices dated in the selected year. In All Years: every invoice (Python kept
  only paid ones; see "Fixed in 2.0").
- Cards:
  - Revenue collected: Σ amount_paid.
  - Invoices: count, with N paid and N partial.
  - Still outstanding: Σ balance_due, shown when > 0.
  - Single year only: Avg / active month = total ÷ (months with revenue > 0, min 1).
  - Single year only: vs previous year = total − previous total, with `(ytd%)` when the
    year is the current one and the previous year is > 0.
- Bar chart: months Jan-Dec (single year) or years ascending (All Years), of amount_paid.
  Fill `rgba(28,52,88,.85)`.
- Doughnut: Σ amount_paid by customer **name**, descending. When there are more than 8,
  show the top 7 plus "Other". 15-colour palette in `design-spec.md`. Cutout 62%.
- Tables:
  - Monthly breakdown (months with invoices): count, invoiced Σ total, collected.
  - Year by year: collected and change. Change shows only when this year and the previous
    row are both > 0.
  - Top customers: rank, name, collected, share %.

### Lists

- Customers: search on name contains (case-insensitive), "Show inactive", sorted by name.
  Columns: name/attn, city and state, phone, email, outstanding, status.
- Invoices:
  - Tabs All / Unpaid / Paid filter on the `paid` flag (unpaid includes partial). The
    unpaid badge counts balance > 0.
  - Search on number or customer name. Sorted by date desc, then number desc.
  - Header: "N shown · M with balance ($X)" across all invoices.
  - Footer: Σ total and −Σ balance.
- Work orders:
  - "Open" shows only orders with open work; "All" shows every order.
  - Search on customer, number, line description or label.
  - One collapsible card per customer: completed lines then pending lines.
- Items: "Show inactive", sorted by description; actions edit, toggle active, delete.

### Backup and restore (`database_mgr.py`)

- Backup: copy the SQLite file to `profitdjinn_backup_YYYYmmdd_HHMMSS.db`. The port uses the
  SQLite online-backup API instead of a raw file copy.
- New in 2.1 (no 1.x equivalent): the start-up backup reminder, `Services/BackupReminder.cs`.
  Settings `backup_reminder_enabled` (default true), `backup_reminder_days` (default 7, 1 to
  365) and `backup_reminder_last` (YYYY-MM-DD, hidden). Due when enabled and today minus last
  is at least the interval. A missing or unreadable `last` starts the countdown instead of
  asking. Either answer to the popup, or any saved backup, sets `last` to today. Shown once
  per start, after unlock when there is an app password.
- New in 2.1: the upgrade backup, `Services/UpgradeBackup.cs`, run by `Store` before
  `Schema.Ensure`. `db_app_version` records the version that last opened the file; when it
  differs from the running version (blank counts, so 1.x and 2.0 files qualify), the file is
  copied to `backups\pre-upgrade_<yyyyMMdd_HHmmss>_from-<old|older>_to-<new>.db` first. A
  new, empty file is not copied. The newest five are kept. If the copy fails, the app stops
  with a message and the database is not touched.
- Analyze a chosen file:
  - It must start with `SQLite format 3`.
  - List tables both sides; missing and extra tables; missing columns per shared table;
    row counts.
  - `incompatible` if `users{id,username,email,password_hash}`, `settings{id,key,value}` or
    `roles{id,name}` is missing a table or column. Otherwise `compatible` when nothing is
    missing, else `needs_migration`.
- Apply:
  - Requires an explicit confirm.
  - Re-analyze, and abort if incompatible.
  - Write a safety copy `<db>.pre_restore_YYYYmmdd_HHMMSS` next to the live DB.
  - Replace the whole file.
  - Run the startup steps (not the first-run seed).
  - On any error, copy the safety file back and say so. If that also fails, give the safety
    copy's path.

### Invoice PDF (`utils/pdf_generator.py:66-268`)

- A4 portrait, mm, margins 15. The header starts at y = 15 (measured on a real 1.x PDF; reading the code suggested 10).
- Header:
  - Left: company name, Helvetica Bold 18, h10.
  - Right: "INVOICE", Bold 20, right-aligned.
  - Left address lines, 10pt, 5 mm apart from y+11: address; `"{city}, {state} {zip}"`
    trimmed of ", "; email; phone.
  - Right: "Invoice #: X" at y+12 and "Date: YYYY-MM-DD" at y+18, h6, right-aligned.
  - Then `y = max(left_y + 3, top + 28)`, a gray (180) rule, and 5 mm.
- Bill To: "Bill To:" Bold 10 h6, 1 mm. Then 10pt lines, h5.5: name; "Attn: …"; address;
  `", ".join(city, state, zip)`. Then 7 mm.
- Table:
  - Column widths 8/44/11/18/rest % of 180 mm: Line #, Description, Qty, Unit Price, Total.
  - Header fill navy (28,52,88), white Bold 9, h7.5, centred / left / centred / right /
    right.
  - Row height `max(7, wrapped_lines × 5.5 + 1.5)`. The description wraps; the other cells
    are top-aligned.
  - Even rows fill (249,250,251), odd rows white; bottom border gray 220.
  - Values `qty:g`, `$unit:,.2f`, `$amount:,.2f`.
  - A row that would cross `page height − 30` starts a new page with the header redrawn.
- Totals, right-aligned under the last two columns, after 5 mm:
  - With credit: "Subtotal:", "Credit: -$x", then "Amount Due:" in Bold 11.
  - Otherwise: "Total:" in Bold 11.
- Then 8 mm, notes ("Notes:" Bold 9 + 9pt text, line height 5, kept together), then terms
  (9pt, gray 80).
- Footer: "Thank You for Your Business!", Bold 11, centred at `page height − 20`, last page
  only.
- No payments or balance on the PDF. File name `{invoice_number}.pdf`.

## Fixed in 2.0 (do not copy)

- Numbering sorts the suffix as a number, not as text (`JQ999` vs `JQ1000`).
- Invoice edit capped applied credit, and recalculated paid status, against the **old**
  lines. 1.x deleted and re-added lines without refreshing the loaded collection, so the
  cap never fired. Found by the parity test: $50 credit stayed on an invoice cut to $40,
  and the customer lost $10 of credit. 2.0 uses the new lines.
- A line-builder quantity of 0 became 1 (`parseFloat(x) || 1`). On the bill screen's
  "Every line" mode that billed a labor line logged at 0 hours as a full hour at the rate.
  2.0 keeps 0 as 0; only a blank or unreadable quantity counts as 1 (Jon, 2026-10-02).
- The Item form's Delete button submitted the edit form (nested `<form>`).
- An Item price of $0 was rejected (`DataRequired` treats 0 as empty).
- The payment dialog's default date was a UTC date (tomorrow, on US evenings).
- Revenue "All Years" left out unpaid invoices.
- Editing an invoice whose customer had been made inactive lost the customer.
- Removing the login logo or app icon did not survive a restart.
- The orphaned billed line (`billed` with no invoice) displayed as "—". It now displays as
  completed throughout.

## Dropped in 2.0

Login, first-start setup, users, roles and permissions, the audit log screen, Admin
overview, and the external-database configuration. One optional app password replaces
sign-in. The tables stay, so a 1.x build can still open the file.

## Expenses (new in 2.2, no 1.x equivalent)

Optional; the `expenses_enabled` setting (default `false`) shows Vendors and Expenses in the
sidebar. Turning it off hides them and keeps the data. The 1.x tables are untouched; the six
new tables follow the 1.x conventions (no AUTOINCREMENT, money FLOAT, DATE text, BOOLEAN 0/1).

- **Vendors:** name required; contact, address, phone, email, default category, notes,
  active. A vendor with expenses or recurring expenses cannot be deleted, only made inactive.
- **Categories:** 17 starters (Schedule C style, "Other" last), seeded once. Names unique
  ignoring case; new ones go before "Other". In use: hide only. Hidden: kept on old
  expenses, not offered on new ones.
- **Expenses:** vendor optional, category, date, optional due date (not before the date),
  description, reference, amount in whole cents above zero, notes. "Already paid" on a new
  expense records one full payment. Status Paid / Partial / Unpaid from payments only;
  Overdue when a balance remains past the due date.
- **Payments:** any number, each at most the balance due; an edit cannot lower the amount
  below what is paid. Methods are the invoice payment methods.
- **Receipts:** PDF or image, 25 MB max, copied into `receipts_folder` (empty = the data
  folder's `receipts`). Changing the folder offers to move existing files; "Leave Them" keeps
  them openable where they are. Deleting an expense or a receipt deletes ProfitDjinn's copy.
- **Recurring:** monthly or yearly, start date, day of month (short months use their last
  day), optional end date, auto-paid (with a method) or bill (due on its date). Created at
  start up to today, only while Expenses is on; a start date in the past asks before
  back-filling. Never creates a date twice or re-creates a deleted one; resuming a paused
  template skips the paused dates; deleting a template keeps its expenses.

### Profit & Loss (new in 2.2)

- One year at a time, every year with income or expenses listed. **Cash** (default): income
  is each payment received on its date, plus invoices marked paid without payment rows at
  their net total on `paid_date` (else the invoice date); expenses are expense payments on
  their dates. **Accrual**: invoice totals on invoice dates, expense amounts on expense
  dates, paid or not. Applying account credit is not income.
- Shows income, expenses, net (profit or loss), margin (net / income), last year's net,
  months, expenses by category and by vendor, and year by year.
- Exports: P&L CSV (months, total, categories), expenses CSV (every entry of the year),
  one-page PDF. CSV is UTF-8 with a BOM, RFC 4180 quoting, and a leading = + @ is prefixed
  with ' so a spreadsheet does not run it.
- Dashboard: with Expenses on, a "{year} Net Profit" (or Net Loss) tile, cash basis, opens
  the page; the six tiles sit in two rows of three.
- Not the Revenue page's "collected" figure, which keeps 1.x's rule (AmountPaid by invoice
  date).

## New in 2.3

- **Settings** is one section at a time (Business, Invoices, Work Orders, Expenses,
  Appearance, Security & Lock Screen, Backup, Updates & About, and Advanced for any setting
  no section claims), with plain labels. Edits are held until **Save Changes** on a bar pinned
  above the footer; **Discard** drops them; leaving with unsaved edits asks first. Save checks:
  accent colour is a hex colour, backup days 1-365, starting numbers are whole numbers, the
  hourly rate is an amount of 0 or more, and the business name is not empty. Theme is now
  also chosen here. Image uploads and the app password still act at once.
- **Update check:** settings `update_check_enabled` (default true), `update_last_check`,
  `update_latest_version`, `update_latest_url` (the last three hidden). At start, at most once
  a day, an anonymous GET to the GitHub API for the latest release of the public repo; a
  failure is silent and retried next start. When the stored version is newer than the
  running one (semantic order; 2.3.0 is newer than 2.3.0-beta), the footer shows
  "Update available: vX" opening the release page. Settings > Updates & About has Check Now.

## New in 2.4

- **Recurring invoices** (no 1.x equivalent; three new tables, no 1.x table changed):
  `recurring_invoices` (customer, `interval` month/year, `interval_count` 1, start date, day of
  month, optional end date, notes, terms, `collection_method` `send_invoice`, active,
  `generated_through`), `recurring_invoice_lines` (description, quantity, extended amount, in
  order) and `recurring_invoice_runs` (one row per schedule and date, unique, with the invoice
  it made; the invoice link is cleared, not the row, when that invoice is deleted).
- Dates: the shared `Rules/Schedule` (same rule as recurring expenses: from the start month,
  day clamped to the month's last day). Next date = first occurrence after
  `generated_through` (or the start date).
- **Creating:** at every start (after unlock), and after a schedule is saved, every date from
  the next one through today is created: next invoice number, the scheduled date, unpaid,
  lines with amounts rounded to cents, `{month}`/`{year}` (any case) filled from the date in
  line descriptions and notes. A schedule whose customer is inactive or deleted is held back
  with a warning naming it. Saving a schedule whose first date is today or past asks first
  ("Create N invoices now?").
- **Issue early** (Upcoming page Print, PDF, Issue Now; the Invoices page send button): only
  the next date, which keeps its date; the schedule moves on. Print and PDF issue first, then
  open the real invoice and print it. **Skip** sets `generated_through` to that date.
- Pausing creates nothing; turning back on skips the paused dates. Editing changes future
  invoices only. Deleting a schedule keeps its invoices; deleting the customer deletes its
  schedules (with its invoices, as before).
- **Dashboard:** the first tile is "Next 30 Days" (2.4.0 said "Upcoming (30 days)"): the number of invoices the active
  schedules will create from now through 30 days ahead (a date already due but not yet
  created counts), badge = their total; muted at 0; opens Invoices.
- **Invoices page:** once any schedule exists, an **Upcoming** card (each active schedule's
  next invoice, soonest first) sits above the tabs, and the list card is titled All Invoices.
  Header button **Recurring** opens the list of schedules.
- Accessibility: icon-only buttons take their tooltip as the accessible name, and text links
  are UI Automation hyperlinks that can be invoked (the smoke test relies on both).
- Stripe readiness: see `docs/stripe-readiness.md`.

## New in 2.5

- **Business settings start empty.** 1.x and 2.0-2.4 seeded sample text ("Your Name",
  "123 Main Street", "Anytown", "ST", "00000", "you@example.com", "(555) 555-0100", and
  "Make all checks payable to Your Name" as terms line 2) that a new user had to delete. The
  defaults are now empty and Settings > Business shows examples as placeholder hints. At every
  start a setting still holding exactly its old sample is cleared (`Seed.ClearOldSamples`);
  anything else is left alone. The business name is no longer required to save Settings; the
  top bar falls back to the app name. Terms line 1 keeps its useful default.
- **Settings > Features:** "Work orders" (`workorders_enabled`, default on) and "Track
  expenses" (moved here from the Expenses section). Work orders off hides the sidebar item,
  the customer page's Work Order button and card, and the dashboard's Unbilled Work tile;
  work order routes open the Dashboard. Data is kept. The dashboard shows up to five tiles in
  one row and six as two rows of three.
- **Service dates per invoice line** (optional From/To, behind the calendar button on each
  line in the invoice form and the bill screen). Stored in `invoice_line_service`
  (line_id, invoice_id, description, service_start, service_end), not on 1.x's invoice_lines;
  a row only applies while its line still has that id, invoice and description, and orphans
  are removed at start, so a line 1.x deletes or edits loses its dates instead of passing them
  to another line. Shown under the description: "Service: 09/01/26 - 09/30/26" (one date:
  "Service: 09/15/26"), on screen and on the PDF (8.5 pt grey line). An end before the start
  is refused ("Line N: ...").
- **Recurring lines can bill the period** (`recurring_invoice_lines.bill_period`): each
  invoice gets the period it covers as that line's service dates, from its date to the day
  before the schedule's next date (monthly on the 1st: Nov 1 - Nov 30; yearly: 12 months).
- **PDF preview:** Preview on the invoice, upcoming-invoice and Profit & Loss pages opens the
  PDF in a ProfitDjinn window (PDFium at 150 dpi, zoom, Ctrl+wheel, Fit Width) with Save PDF
  and Print; nothing is written to disk unless saved. On an upcoming invoice, Save and Print
  from the preview issue it first, like the page's own buttons.
- **Fixed:** Edit on a recurring invoice with no end date crashed (2.4.0); the invoice form had
  the same fallback pattern for empty notes and terms. Empty states lost their top spacing
  under a table header. Status pills made detail-page headers taller than the rest. P&L's
  explanation ran under its export buttons. Revenue wrapped a fifth tile onto its own row.
  Charts side by side are now the same height. Rows built up one button at a time had no gaps.
  The collapsed sidebar's icons were off centre.

## New in 2.6

- **Customer and vendor boxes are type-to-pick** (invoice, recurring invoice, expense and
  recurring expense forms). Typing lists names containing the text, names starting with it
  first, and fills in the rest of the best match (selected, so typing on replaces it; Tab or
  Enter accepts; Backspace/Delete never fill in). An exact name, any case, picks the record.
  The arrow, or focusing an empty box, lists everyone. An inactive customer or vendor already
  on an edited record stays listed, marked "(inactive)". Vendor stays optional: an empty box
  means no vendor.
- Text that matches no one shows **Add Customer "X"** / **Add Vendor "X"** under the box. It
  opens a dialog with the name filled in (customer: contact, email, phone, address; vendor:
  contact, email, phone, default category), creates the record and picks it, without leaving
  the form. A quick-added vendor's default category is applied to the expense. Saving with
  unmatched text is refused with a field error naming the Add button.
- **Cost of revenue:** a category can count as cost of revenue (Categories, "Counts As"). Once
  any does, the P&L shows Income, Cost of Revenue, Gross Profit (with gross margin), Operating
  Expenses, Net Profit and Net Margin, the month table gains Cost / Gross / Operating, and the
  PDF and CSV follow. Net profit is the same either way.
- **Recurring revenue:** the Recurring Invoices page shows Monthly Recurring (active schedules
  with invoices to come; a yearly one counts a twelfth), Yearly Recurring (x12) and Active
  Clients. Read only.
- **Who paid an expense:** each payment is Business (optionally from a bank account), Personal
  funds (owner, to be paid back) or No cash. Owner-paid amounts show as "Owed to You" (tile and
  Owner-paid tab) until Mark Paid Back, which can record the payback from an account. Who paid
  never changes the P&L.
- **Mileage:** New Expense > Mileage: miles x the mileage rate (Settings > Expenses, default
  0.70; the user keeps it at the IRS rate), paid in full with no cash. The trip keeps its rate;
  editing the miles recomputes the amount.
- **Bank accounts** (Settings > Features, off by default): checking, savings, credit card,
  personal and payment-processor accounts with an opening balance. Money In (owner
  contribution, other deposit), Money Out (fee, owner draw, other), transfers and processor
  payouts (two linked rows), pending / cleared / reconciled, a running-balance register, and
  Reconcile (statement date and balance, tick rows, finish at a zero difference). A fee can
  also be recorded as an expense (one row). Payments can name an account: "Deposited To" on
  invoice payments (Bank Accounts on only), "Paid From" on expense payments; the linked row
  goes when the payment goes. The P&L never reads bank data.

## New in 2.7

- **Settings > Features** gains **Revenue report** and **Service items**, both on by default
  (`revenue_enabled`, `items_enabled`; new settings rows only). Revenue off hides the Revenue
  page and its sidebar entry; the dashboard keeps the year's revenue figure, but the tile no
  longer opens anything. Items off hides Items and its sidebar entry and the "Quick-add
  service" list on the invoice and recurring invoice forms; saved items are kept. Their routes
  open the Dashboard while off, like work orders and expenses.
- **Profit & Loss drill-down.** Every figure opens a page listing what makes it up: the tiles
  (Margin opens Net), each month's Income / Cost / Gross / Operating / Expenses / Net and the
  Total row, each Expense Category and Top Vendor row, Year by Year's Income / Expenses / Net,
  a bar in the chart (that month's Net) and a doughnut slice (that category; "Other" opens the
  categories grouped into it). Cells showing "-" open nothing.
- The page shows the figure exactly as the P&L shows it (read from the report, not added up
  again), a plain-words note on how it is worked out for the basis, the sum for Gross and Net
  ("Income $X - Expenses $Y = $Z"), then the income entries (payments, or invoices on accrual;
  each opens its invoice) and the expense entries (each opens its expense). Applied account
  credit never appears as income, as on the P&L.
- Figures in the month and year tables keep their normal colour and show an underline on
  hover (`Ui.DrillText`); the charts show a hand cursor over a bar or slice.
- **2.7.1:** month names in the Monthly Breakdown open that month (Net). Detail pages for Net and
  Gross show Income, Expenses (or Cost of Revenue) and the result as tiles, the first two
  clickable, and every detail page lists By Customer, By Category and By Vendor (when there is
  more than one) to narrow further, down to the single invoice or expense. A narrowed page's
  figure is the total its line showed.
- **Back returns to where the page was scrolled**, everywhere in the app (browser-like). The
  detail page's button is now "Back".

