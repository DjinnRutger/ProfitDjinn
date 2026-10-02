# ProfitDjinn

Invoicing and customer management for a one-person business, as a Windows desktop app.
Track customers, log work on a rolling work order as you do it, turn finished work into
invoices, record payments and account credit, and print or save invoice PDFs.

**Download:** get `ProfitDjinn.exe` from the [Releases](https://github.com/DjinnRutger/ProfitDjinn/releases)
page and run it. It is one file with nothing to install: .NET is built in. Windows 10 or 11.
Windows SmartScreen may warn because the file is not code-signed; choose More info > Run anyway.

## Using it

- **First start** opens the dashboard on an empty database. Put your business name, address
  and payment terms on your invoices under **Settings > Invoices**, and your hourly rate under
  **Settings > Work Orders**.
- **Customers**: contact details, notes, and totals (invoiced, paid, outstanding, credit).
- **Work Orders**: each customer has one rolling tab. Add to-dos, log work (hours x rate, or
  parts and services), mark lines no-charge, then **Bill** them onto an invoice as one line,
  grouped by type, or line by line. Anything not billed stays on the tab.
- **Invoices**: line items, partial payments, overpayments that become account credit,
  applying credit to another invoice, Print and PDF.
- **Revenue**: collected revenue by month or year, by customer.
- **Items**: a price list for quick-adding common lines.
- **Settings > Security** sets an optional app password, asked for at start (and by **Lock**
  in the sidebar). It keeps casual eyes out on a shared PC; it does not encrypt the data.
- **Themes**: Light, Dark and Terminal, from the button at the top right.

## Your data

Everything is in one SQLite file, `%LOCALAPPDATA%\ProfitDjinn\app.db`. Replacing or deleting
`ProfitDjinn.exe` never touches it.

- **Backup & Restore** (sidebar) saves a complete copy and restores one. A restore checks
  the file first, needs a confirm, and keeps a safety copy of your current data next to it
  (`app.db.pre_restore_<date>`), put back automatically if the restore fails.
- **Backup reminder** (on by default, every 7 days): when ProfitDjinn opens, it asks you to
  save a backup. "Back Up Now" or "Not Now", it then waits the full interval again; making a
  backup from Backup & Restore also restarts the count. A new install waits the interval
  before the first reminder. Turn it off or change the days in Settings > Backup.
- To move to a new PC: back up, install the exe there, restore.
- Custom logo and sidebar icon: `%LOCALAPPDATA%\ProfitDjinn\branding\`.

## Coming from 1.x

2.0 opens the 1.x database where it already is, unchanged; the first start only adds two
settings. 1.x can still open the file afterwards, so going back is safe. Users and roles are
gone: one optional app password replaces sign-in. The bugs 2.0 fixes on the way are listed
in `docs/port-spec.md` under "Fixed in 2.0".

## Building from source

Needs the .NET 8 SDK (or later).

```
.\build.ps1                          # runs the tests, then publishes the single exe
dotnet test tests\ProfitDjinn.Tests  # 65 tests
dotnet run --project src\ProfitDjinn.App
```

Output goes to `artifacts\publish\ProfitDjinn.exe`, or wherever a `Directory.Build.local.props`
sets `DevRoot`. To try a build without touching your real data, set
`PROFITDJINN_DATA_DIR` to an empty folder first; the window title then says `[test data]`.

| Folder | What it is |
| --- | --- |
| `src/ProfitDjinn.Core` | data, business rules, services, invoice PDF |
| `src/ProfitDjinn.App` | the WPF app: shell, themes, controls, screens |
| `tests/ProfitDjinn.Tests` | unit and parity tests |
| `tools/` | parity harness against 1.x, icon converter, smoke tests |
| `docs/` | behaviour and design specs for the port |
| `legacy/` | the 1.x Flask app, kept for reference and as the rollback path |

## License

MIT. See `LICENSE`. Bundled: Bootstrap Icons (MIT), Inter and VT323 fonts (SIL OFL), PDFsharp
(MIT), PDFium (BSD-3/Apache-2.0) via bblanchon.PDFium, Dapper (Apache-2.0),
Microsoft.Data.Sqlite (MIT).
