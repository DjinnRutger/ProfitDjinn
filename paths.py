r"""Where ProfitDjinn keeps its data.

One source of truth for every writable path the app needs. Two modes:

    Frozen EXE   ->  %LOCALAPPDATA%\ProfitDjinn
    From source  ->  <project>\instance

Nothing here hard-codes a username. The frozen location comes from the
LOCALAPPDATA environment variable, so the same build works on any machine
and any Windows account.

This module lives at the project root rather than inside app/ on purpose.
run_gui.py has to know these paths *before* it imports the app package,
because app/config.py reads SECRET_KEY and DATABASE_URI out of the
environment while its class bodies are being evaluated at import time.
Importing app.paths would trigger app/__init__.py and lose that race.
Keep this module free of any app imports.
"""
import os
import secrets
import sys
from pathlib import Path

APP_NAME = "ProfitDjinn"

_PROJECT_ROOT = Path(__file__).resolve().parent


def is_frozen() -> bool:
    """True when running from the PyInstaller-built EXE."""
    return getattr(sys, "frozen", False)


def data_dir() -> Path:
    """The folder holding the database, secret key, and browser profile.

    Created if it does not exist.
    """
    if is_frozen():
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            raise RuntimeError(
                "The LOCALAPPDATA environment variable is not set, so ProfitDjinn "
                "cannot work out where to keep its database. This variable is "
                "standard on Windows and normally points at "
                r"C:\Users\<you>\AppData\Local."
                " Set it and start ProfitDjinn again."
            )
        directory = Path(local_app_data) / APP_NAME
    else:
        directory = _PROJECT_ROOT / "instance"

    directory.mkdir(parents=True, exist_ok=True)
    return directory


def database_path() -> Path:
    return data_dir() / "app.db"


def database_uri() -> str:
    """SQLAlchemy URI for the live database. Forward slashes, as SQLAlchemy needs."""
    return "sqlite:///" + database_path().as_posix()


def webview_profile_dir() -> Path:
    """Where the WebView2 window keeps cookies and local storage.

    This is what makes "Keep me signed in" survive a restart. Deleting this
    folder signs the user out; nothing else is lost.
    """
    directory = data_dir() / "webview"
    directory.mkdir(parents=True, exist_ok=True)
    return directory


def load_or_create_secret_key() -> str:
    """Return the persistent SECRET_KEY, generating it on first run.

    The key must stay stable across restarts. Flask-Login signs the
    "remember me" cookie with it, so a new key silently signs the user out
    of every existing session.
    """
    key_file = data_dir() / ".secret_key"

    if key_file.is_file():
        key = key_file.read_text(encoding="utf-8").strip()
        if key:
            return key

    key = secrets.token_hex(32)
    key_file.write_text(key, encoding="utf-8")
    return key
