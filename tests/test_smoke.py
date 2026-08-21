"""Does the app boot, seed itself, and render every page it offers?

This is the smoke test the auto-push rule depends on. If it fails, nothing
gets pushed.
"""
from conftest import AUTHENTICATED_ROUTES, SEEDED_PASSWORD, SEEDED_USERNAME


def test_seeds_on_first_run(app):
    with app.app_context():
        from app.models.permission import Permission
        from app.models.role import Role
        from app.models.setting import Setting
        from app.models.user import User

        assert User.query.filter_by(username=SEEDED_USERNAME).first() is not None
        assert Role.query.filter_by(name="Administrator").first() is not None
        assert Permission.query.count() > 0
        assert Setting.query.filter_by(key="app_name").first().value == "ProfitDjinn"


def test_admin_has_every_permission(app):
    """_ensure_permissions() must grant new permissions to full-access roles.

    Miss this and a feature ships invisible: the routes exist but the sidebar
    never shows them.
    """
    with app.app_context():
        from app.models.permission import Permission
        from app.models.role import Role

        admin_role = Role.query.filter_by(name="Administrator").first()
        granted = {p.name for p in admin_role.permissions}
        everything = {p.name for p in Permission.query.all()}

        assert everything - granted == set(), "administrator is missing permissions"


def test_login_page_renders(client):
    response = client.get("/auth/login")
    assert response.status_code == 200
    assert b"Sign In" in response.data


def test_rejects_a_bad_password(client):
    response = client.post(
        "/auth/login",
        data={"username": SEEDED_USERNAME, "password": "not-the-password"},
        follow_redirects=True,
    )
    assert response.status_code == 200
    assert b"Invalid username or password" in response.data


def test_unauthenticated_requests_are_redirected_to_login(client):
    response = client.get("/customers/")
    assert response.status_code == 302
    assert "/auth/login" in response.headers["Location"]


def test_every_authenticated_route_renders(signed_in_client):
    failures = []
    for route in AUTHENTICATED_ROUTES:
        response = signed_in_client.get(route, follow_redirects=True)
        if response.status_code != 200:
            failures.append(f"{route} -> HTTP {response.status_code}")
    assert not failures, "routes failed to render: " + ", ".join(failures)


def test_no_page_still_says_localvibe(signed_in_client):
    """The project grew out of a scaffold called LocalVibe. Nothing should say so."""
    offenders = [
        route
        for route in AUTHENTICATED_ROUTES
        if b"LocalVibe" in signed_in_client.get(route, follow_redirects=True).data
    ]
    assert not offenders, "stale scaffold name rendered on: " + ", ".join(offenders)
