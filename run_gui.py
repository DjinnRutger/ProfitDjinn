"""
ProfitDjinn – Desktop GUI entry point.

When run normally (dev):   python run_gui.py
When frozen (EXE):         ProfitDjinn.exe

FlaskWebGUI opens the app in a Chrome/Edge app-mode window with no
address bar or browser chrome — looks and feels like a native app.
"""
import os
import sys
import secrets

# ── Frozen-EXE bootstrap (must run before any app imports) ───────────────────
# When PyInstaller bundles the app, sys.frozen is True and sys.executable
# points to ProfitDjinn.exe.  We need to:
#   1. Point the database at a writable folder next to the EXE (not _MEIPASS)
#   2. Generate / restore a persistent SECRET_KEY
#   3. Tell Flask where to find its instance folder
if getattr(sys, "frozen", False):
    BASE_DIR = os.path.dirname(sys.executable)

    # Instance folder lives next to the EXE so the database persists
    INSTANCE_DIR = os.path.join(BASE_DIR, "instance")
    os.makedirs(INSTANCE_DIR, exist_ok=True)
    os.environ["FLASK_INSTANCE_PATH"] = INSTANCE_DIR

    # Database path (forward slashes required for SQLAlchemy on Windows)
    db_path = os.path.join(INSTANCE_DIR, "app.db").replace("\\", "/")
    os.environ["DATABASE_URI"] = f"sqlite:///{db_path}"

    # Persist a random SECRET_KEY between runs
    key_file = os.path.join(BASE_DIR, ".secret_key")
    if os.path.exists(key_file):
        with open(key_file, "r") as f:
            os.environ.setdefault("SECRET_KEY", f.read().strip())
    else:
        key = secrets.token_hex(32)
        with open(key_file, "w") as f:
            f.write(key)
        os.environ["SECRET_KEY"] = key

# ── Create app ────────────────────────────────────────────────────────────────
from app import create_app  # noqa: E402  (imports after sys.path setup)

app = create_app("gui")

# ── Launch ────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    from flaskwebgui import FlaskUI

    FlaskUI(
        app=app,
        server="flask",
        width=1440,
        height=900,
        browser_path=None,   # auto-detect Edge/Chrome
    ).run()
