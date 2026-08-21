# ProfitDjinn

Flask-based invoicing and customer management. Tracks customers, logs work orders as
the work happens, turns completed work into invoices, records payments and account
credit, and prints invoice PDFs. Runs as a desktop app window or a local dev server.

## Requirements

- Python 3.13
- Windows (the desktop window and the EXE build are Windows-only; the web app itself
  is not)

## Setup

The virtual environment lives outside this folder so OneDrive never syncs it. Same
path on both machines:

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements.txt
```

Create a `.env` in the project root (it is gitignored — never commit it):

```
SECRET_KEY=<generate: python -c "import secrets; print(secrets.token_hex(32))">
DATABASE_URI=sqlite:///instance/app.db
FLASK_ENV=development
```

## Run

```
C:\Dev\venvs\ProfitDjinn\Scripts\python run.py        # dev server at http://localhost:5000
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py    # desktop window
```

First run creates `instance/app.db` and seeds an administrator account. The seeded
credentials are in `app/__init__.py`; change the password immediately.

## Build the Windows EXE

```
build.bat
```

Output lands in `dist\ProfitDjinn\`. Distribute the whole folder. The `instance\`
folder created next to the EXE holds the database and must travel with it.

**Known issue:** `build.bat` looks for `.venv\Scripts\activate.bat` inside the project
folder and will fail, because the venv now lives at `C:\Dev\venvs\ProfitDjinn\`. Either
point the script at that path or activate the venv yourself and call
`pyinstaller profitdjinn.spec --clean --noconfirm` directly.

## Paths this project expects

| Path | Contents | Synced |
| --- | --- | --- |
| `Software Dev\ProfitDjinn\` | source, templates, config, `.git` | yes, OneDrive |
| `C:\Dev\venvs\ProfitDjinn\` | virtual environment | no |
| `instance\app.db` | live SQLite database | currently yes — see status |
| `dist\`, `build\`, `__pycache__\` | build output and bytecode | currently yes — see status |

## Status

Working and in use. Open items:

- **No automated tests.** Verification is manual only.
- **`migrations/` is empty.** Flask-Migrate is installed but never initialized. New
  columns must be added by hand to `_run_migrations()` in `app/__init__.py` or they
  will not appear on the existing database. See `CLAUDE.md`.
- **`instance/app.db` is inside OneDrive.** SQLite in a synced folder can corrupt.
  Fix is one line per machine in `.env`:
  `DATABASE_URI=sqlite:///C:/Dev/ProfitDjinn/data/app.db` — move the file, don't copy it.
- **`dist/` is ~74 MB inside OneDrive**, plus `build/` and `__pycache__`. Build output
  belongs in `C:\Dev\`. All are gitignored, so this costs sync time, not repo size.
- **`build.bat` venv path** (above).
