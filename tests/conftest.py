"""Shared test fixtures.

The environment has to be set before `app.config` is imported for the first
time: its class bodies read SECRET_KEY and DATABASE_URI out of os.environ at
import time. pytest imports conftest.py before any test module, so this is
the one place early enough to redirect the tests at a throwaway database.
Nothing here touches the real instance/app.db.
"""
import os
import sys
import tempfile
from pathlib import Path

_PROJECT_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(_PROJECT_ROOT))

_TMP_DIR = Path(tempfile.mkdtemp(prefix="profitdjinn-tests-"))

os.environ["DATABASE_URI"] = "sqlite:///" + (_TMP_DIR / "test.db").as_posix()
os.environ["SECRET_KEY"] = "test-only-secret-key-never-used-in-a-real-build"
os.environ["FLASK_INSTANCE_PATH"] = str(_TMP_DIR)

import pytest  # noqa: E402

from app import create_app  # noqa: E402

# Every route reachable from the sidebar once signed in as an administrator.
AUTHENTICATED_ROUTES = [
    "/",
    "/profile",
    "/revenue",
    "/customers/",
    "/invoices/",
    "/items/",
    "/work-orders/",
    "/admin/",
    "/admin/users",
    "/admin/roles",
    "/admin/settings",
    "/admin/audit",
    "/admin/database",
]

SEEDED_USERNAME = "admin"
SEEDED_PASSWORD = "Admin@1234!"


@pytest.fixture(scope="session")
def app():
    """The app under the same config the desktop EXE uses.

    Testing the "gui" config rather than "testing" is deliberate: the cookie
    durations that make "Keep me signed in" work only exist on GUIConfig, and
    those are what test_stays_signed_in.py is about.
    """
    application = create_app("gui")

    # Both of these are read per-request, so switching them off after the app
    # is built works and keeps GUIConfig itself honest.
    application.config["WTF_CSRF_ENABLED"] = False
    application.config["RATELIMIT_ENABLED"] = False

    return application


@pytest.fixture
def client(app):
    return app.test_client()


@pytest.fixture
def signed_in_client(client):
    """A client that has completed a real login through the form."""
    response = client.post(
        "/auth/login",
        data={
            "username": SEEDED_USERNAME,
            "password": SEEDED_PASSWORD,
            "remember_me": "y",
        },
        follow_redirects=True,
    )
    assert response.status_code == 200, "login request failed"
    assert b"Sign In" not in response.data, "still on the login page after posting valid credentials"
    return client
