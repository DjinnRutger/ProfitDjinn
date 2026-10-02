"""First start: no built-in login, the installer chooses one.

Up to 1.0.0-beta a fresh database got an "admin" account whose password was in
the public source. Now the seed creates no user at all and every page goes to
/auth/setup until the first administrator exists. Installs that still use the
old password are made to change it at their next sign-in.

Each test builds its own app on its own empty database, so these never touch
the shared session app the other tests use.
"""
import pytest

from conftest import complete_setup, make_app
from app.blueprints.auth import LEGACY_DEFAULT_PASSWORD
from app.version import __version__


@pytest.fixture
def fresh_app(tmp_path):
    return make_app(tmp_path / "fresh.db")


def _users(application):
    with application.app_context():
        from app.models.user import User
        return User.query.all()


def _sign_in(client, username, password):
    return client.post("/auth/login", data={"username": username, "password": password},
                       follow_redirects=False)


def test_fresh_install_has_no_user_and_no_default_password(fresh_app):
    assert _users(fresh_app) == []


@pytest.mark.parametrize("path", ["/", "/auth/login", "/customers/", "/admin/users"])
def test_every_page_goes_to_setup_until_an_admin_exists(fresh_app, path):
    response = fresh_app.test_client().get(path)
    assert response.status_code == 302
    assert response.headers["Location"].endswith("/auth/setup")


def test_setup_page_renders_with_version(fresh_app):
    response = fresh_app.test_client().get("/auth/setup")
    assert response.status_code == 200
    assert b"Create Administrator" in response.data
    assert f"v{__version__}".encode() in response.data


@pytest.mark.parametrize("data, message", [
    ({"username": "jo", "email": "a@example.com", "password": "longenough1", "confirm_password": "longenough1"},
     b"Field must be between 3 and 64 characters"),
    ({"username": "owner", "email": "not-an-email", "password": "longenough1", "confirm_password": "longenough1"},
     b"Invalid email address"),
    ({"username": "owner", "email": "a@example.com", "password": "short", "confirm_password": "short"},
     b"Field must be between 8 and 128 characters"),
    ({"username": "owner", "email": "a@example.com", "password": "longenough1", "confirm_password": "different1"},
     b"Passwords must match"),
])
def test_setup_refuses_bad_input_and_creates_nothing(fresh_app, data, message):
    response = fresh_app.test_client().post("/auth/setup", data=data)
    assert response.status_code == 200
    assert message in response.data
    assert _users(fresh_app) == []


def test_setup_creates_an_administrator_and_signs_in(fresh_app):
    client = fresh_app.test_client()
    response = client.post("/auth/setup", data={
        "username": "  jane  ", "email": "jane@example.com",
        "password": "a-good-password", "confirm_password": "a-good-password",
    })
    assert response.status_code == 302
    assert response.headers["Location"].endswith("/dashboard")

    with fresh_app.app_context():
        from app.models.user import User
        user = User.query.one()
        assert user.username == "jane"
        assert user.is_admin and user.role.name == "Administrator"
        assert user.check_password("a-good-password")

    # Signed in already, and every admin page is reachable.
    assert client.get("/admin/users").status_code == 200


def test_setup_is_closed_once_an_admin_exists(fresh_app):
    assert complete_setup(fresh_app).status_code == 302

    client = fresh_app.test_client()
    assert client.get("/auth/setup").headers["Location"].endswith("/auth/login")
    client.post("/auth/setup", data={
        "username": "intruder", "email": "x@example.com",
        "password": "intruder-pass", "confirm_password": "intruder-pass",
    })
    assert [u.username for u in _users(fresh_app)] == ["owner"]


def test_restart_does_not_reseed(tmp_path):
    db_file = tmp_path / "restart.db"
    first = make_app(db_file)
    complete_setup(first)
    second = make_app(db_file)
    with second.app_context():
        from app.models.role import Role
        assert Role.query.filter_by(name="Administrator").count() == 1
    assert second.test_client().get("/auth/login").status_code == 200


def _app_with_legacy_admin(tmp_path):
    """An install made by an older release: admin / the public default password."""
    application = make_app(tmp_path / "legacy.db")
    complete_setup(application, username="admin", password=LEGACY_DEFAULT_PASSWORD)
    return application


def test_legacy_default_password_must_be_changed(tmp_path):
    application = _app_with_legacy_admin(tmp_path)
    client = application.test_client()

    response = _sign_in(client, "admin", LEGACY_DEFAULT_PASSWORD)
    assert response.headers["Location"].endswith("/profile")

    # Every other page bounces back to the profile page.
    for path in ("/", "/customers/", "/admin/users"):
        bounced = client.get(path)
        assert bounced.status_code == 302 and bounced.headers["Location"].endswith("/profile")
    assert client.get("/profile").status_code == 200

    # Re-using the same password does not count as a change.
    client.post("/profile", data={"current_password": LEGACY_DEFAULT_PASSWORD,
                                  "new_password": LEGACY_DEFAULT_PASSWORD,
                                  "confirm_password": LEGACY_DEFAULT_PASSWORD})
    assert client.get("/customers/").status_code == 302

    client.post("/profile", data={"current_password": LEGACY_DEFAULT_PASSWORD,
                                  "new_password": "brand-new-secret",
                                  "confirm_password": "brand-new-secret"})
    assert client.get("/customers/").status_code == 200
    with application.app_context():
        from app.models.user import User
        assert User.query.filter_by(username="admin").one().check_password("brand-new-secret")


def test_legacy_password_change_through_the_settings_dialog(tmp_path):
    """The sidebar's settings dialog uses the JSON endpoint; it must clear the gate too."""
    application = _app_with_legacy_admin(tmp_path)
    client = application.test_client()
    _sign_in(client, "admin", LEGACY_DEFAULT_PASSWORD)

    response = client.post("/api/change-password", json={
        "current_password": LEGACY_DEFAULT_PASSWORD,
        "new_password": "brand-new-secret", "confirm_password": "brand-new-secret",
    })
    assert response.status_code == 200
    assert client.get("/customers/").status_code == 200


def test_a_normal_password_is_not_forced_to_change(signed_in_client):
    assert signed_in_client.get("/customers/").status_code == 200


def test_footer_shows_version(signed_in_client):
    assert f"v{__version__}".encode() in signed_in_client.get("/").data
