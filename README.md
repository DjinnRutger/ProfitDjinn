# ProfitDjinn

Flask-based invoicing and customer management, running as a Windows desktop app. Tracks
customers, logs work orders as the work happens, turns completed work into invoices,
records payments and account credit, and prints invoice PDFs.

The UI runs in an Edge WebView2 window — a real application window, no browser, no
address bar. WebView2 ships with Windows 10 and 11, so there is nothing extra to install.

## Requirements

- Windows 10 or 11
- Python 3.13 (to build or run from source; the built EXE needs nothing)

## Setup

The virtual environment lives outside this folder so OneDrive never syncs it. Same path
on both machines:

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements-dev.txt
```

`requirements.txt` is what the app needs to run; `requirements-dev.txt` adds pytest and
the build tools.

A `.env` is optional and only affects running from source. Without one the app uses
`instance\app.db` and generates its own key.

```
SECRET_KEY=<generate: python -c "import secrets; print(secrets.token_hex(32))">
DATABASE_URI=sqlite:///instance/app.db
FLASK_ENV=development
```

## Run

```
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py    # desktop window
C:\Dev\venvs\ProfitDjinn\Scripts\python run.py        # dev server at http://localhost:5000
```

First run creates the database and seeds an administrator account. The seeded
credentials are in `app/__init__.py`; change the password immediately.

To keep Python's bytecode cache out of OneDrive, set this once per machine:

```
setx PYTHONPYCACHEPREFIX C:\Dev\ProfitDjinn\pycache
```

## Tests

```
C:\Dev\venvs\ProfitDjinn\Scripts\python -m pytest
```

16 tests. They run against a throwaway database in the temp folder and never touch real
data. Green before every commit.

## Build the Windows EXE

```
build.bat
```

Output: `C:\Dev\ProfitDjinn\dist\ProfitDjinn\ProfitDjinn.exe`. Build output lives outside
OneDrive on purpose — it is 50+ MB of regenerable files. To distribute, zip the whole
`ProfitDjinn\` folder.

The EXE is standalone. Deleting and rebuilding it never touches the database.

## Where your data lives

| Path | Contents |
| --- | --- |
| `%LOCALAPPDATA%\ProfitDjinn\app.db` | the database |
| `%LOCALAPPDATA%\ProfitDjinn\.secret_key` | signing key — **do not delete, it signs you out** |
| `%LOCALAPPDATA%\ProfitDjinn\webview\` | the window's cookies, which is what keeps you signed in |

On this machine that resolves to `C:\Users\jonqu\AppData\Local\ProfitDjinn\`.

Running from source instead uses the project's `instance\` folder for all three.

Deleting the `webview\` folder signs you out and loses nothing else. Deleting `app.db`
loses everything — take a backup from Admin → Database first.

## Staying signed in

Tick "Keep me signed in" (it is on by default) and the app will not ask again until you
click Sign Out. Two things make that work, and breaking either one brings the old bug
back:

- `run_gui.py` passes `private_mode=False` and a `storage_path` to `webview.start()`.
  `private_mode` **defaults to `True`**, which throws the cookie away on close.
- `GUIConfig` in `app/config.py` sets a 3650-day `REMEMBER_COOKIE_DURATION`, with
  `SESSION_COOKIE_SECURE` and `REMEMBER_COOKIE_SECURE` off because the window uses plain
  HTTP to `127.0.0.1`.

`tests/test_stays_signed_in.py` covers the server side. The browser side needs a person:

1. Launch the EXE, sign in with the box ticked. No save-password prompt should appear.
2. Close the window completely.
3. Reopen. You should land on the dashboard with no login page.
4. Sign Out, close, reopen — now it should ask for credentials again.

## Status

Working and in use. Open items:

- **`migrations/` is empty.** Flask-Migrate is installed but never initialized. New
  columns must be added by hand to `_run_migrations()` in `app/__init__.py` or they will
  not appear on an existing database. See `CLAUDE.md`.
- **The old `dist\` and `build\` folders in this project are stale** — roughly 74 MB left
  over from the previous build layout. Safe to delete; builds now go to `C:\Dev`.
- **No version stamp in the UI.** Fine while this is a single-user app.
- The invoice PDF has only been checked by eye, not against a test fixture.

### History

Originally shelled out to Chrome via `flaskwebgui`, which handed it a throwaway
`--user-data-dir` and deleted it on exit. That is why sign-in never persisted and Chrome
re-asked to save the password on every launch. Replaced with pywebview on 2026-08-21.
