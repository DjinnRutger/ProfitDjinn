"""
App configuration classes.
Load from .env → fall back to safe defaults.
"""
import os
import sys
from datetime import timedelta
from dotenv import load_dotenv

# A frozen EXE gets its configuration from run_gui.py, which sets the
# environment before this module is imported. Reading a .env that happens to
# sit in whatever directory the EXE was launched from would let a stale file
# override the real database path.
if not getattr(sys, "frozen", False):
    load_dotenv()

# Compute absolute path to the project-root instance/ folder so SQLite always works
_HERE         = os.path.dirname(os.path.abspath(__file__))   # …/app/
_PROJECT_ROOT = os.path.dirname(_HERE)                        # …/D1-Test/
_INSTANCE_DIR = os.path.join(_PROJECT_ROOT, "instance")
_DEFAULT_DB   = "sqlite:///" + os.path.join(_INSTANCE_DIR, "app.db").replace("\\", "/")


class Config:
    # ── Core ────────────────────────────────────────────────────────────────
    SECRET_KEY: str = os.environ.get("SECRET_KEY", "dev-secret-CHANGE-ME")
    SQLALCHEMY_DATABASE_URI: str = os.environ.get("DATABASE_URI", _DEFAULT_DB)
    SQLALCHEMY_TRACK_MODIFICATIONS = False

    # ── Session / Cookies ───────────────────────────────────────────────────
    SESSION_COOKIE_HTTPONLY = True
    SESSION_COOKIE_SAMESITE = "Lax"
    SESSION_COOKIE_SECURE = False          # override True in ProductionConfig
    PERMANENT_SESSION_LIFETIME = timedelta(hours=8)
    REMEMBER_COOKIE_HTTPONLY = True
    REMEMBER_COOKIE_SAMESITE = "Lax"
    REMEMBER_COOKIE_DURATION = timedelta(days=14)

    # ── CSRF ────────────────────────────────────────────────────────────────
    WTF_CSRF_ENABLED = True
    WTF_CSRF_TIME_LIMIT = 3600            # 1 hour

    # ── Rate Limiting ───────────────────────────────────────────────────────
    RATELIMIT_STORAGE_URI = "memory://"
    RATELIMIT_DEFAULT = ["200 per day", "50 per hour"]

    # ── Talisman (security headers) ─────────────────────────────────────────
    TALISMAN_ENABLED = True
    TALISMAN_CONFIG: dict = {
        "force_https": False,                    # LAN app – no HTTPS initially
        "strict_transport_security": False,
        "content_security_policy": {
            "default-src": "'self'",
            "script-src":  ["'self'", "'unsafe-inline'", "cdn.jsdelivr.net"],
            "style-src":   ["'self'", "'unsafe-inline'", "cdn.jsdelivr.net"],
            "font-src":    ["'self'", "cdn.jsdelivr.net", "fonts.gstatic.com"],
            "img-src":     ["'self'", "data:", "https:"],
        },
        "referrer_policy": "strict-origin-when-cross-origin",
    }


class DevelopmentConfig(Config):
    DEBUG = True
    TALISMAN_ENABLED = False               # no header friction while building
    SESSION_COOKIE_SECURE = False


class ProductionConfig(Config):
    DEBUG = False
    SESSION_COOKIE_SECURE = True
    TALISMAN_ENABLED = True


class TestingConfig(Config):
    TESTING = True
    SQLALCHEMY_DATABASE_URI = "sqlite:///:memory:"
    WTF_CSRF_ENABLED = False
    TALISMAN_ENABLED = False
    RATELIMIT_ENABLED = False


class GUIConfig(Config):
    """Standalone desktop app — no console, no browser, local HTTP only."""
    DEBUG = False
    TALISMAN_ENABLED = False        # Not needed for a local desktop window

    # The window talks plain HTTP to 127.0.0.1, so a Secure-only cookie would
    # never be sent back and the user could never stay signed in.
    SESSION_COOKIE_SECURE = False
    REMEMBER_COOKIE_SECURE = False

    # "Keep me signed in" should mean exactly that on a personal desktop app:
    # signed in until Sign Out is clicked. The session cookie dies when the
    # window closes; the remember cookie is what brings the user back.
    REMEMBER_COOKIE_DURATION = timedelta(days=3650)
    PERMANENT_SESSION_LIFETIME = timedelta(days=30)


config: dict = {
    "development": DevelopmentConfig,
    "production":  ProductionConfig,
    "testing":     TestingConfig,
    "gui":         GUIConfig,
    "default":     DevelopmentConfig,
}
