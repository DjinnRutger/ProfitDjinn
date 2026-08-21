"""The regression test for the bug this all started with.

The desktop EXE asked for a sign-in on every launch and "Keep me signed in"
never worked. The cause was flaskwebgui handing Chrome a throwaway
--user-data-dir and deleting it on exit, so the cookie below was written
correctly and then thrown away. The shell is fixed in run_gui.py; these
tests pin down the server half so it cannot rot.

What these tests cannot prove is that the WebView2 window keeps the cookie
across a restart. That needs a built EXE and a human closing the window.
The manual steps are in README.md.
"""
import re
from datetime import datetime, timedelta, timezone
from email.utils import parsedate_to_datetime

from conftest import SEEDED_PASSWORD, SEEDED_USERNAME

REMEMBER_COOKIE = "remember_token"


def _set_cookie_headers(response, name):
    return [h for h in response.headers.getlist("Set-Cookie") if h.startswith(f"{name}=")]


def _expiry_of(cookie_header):
    for part in cookie_header.split(";"):
        key, _, value = part.strip().partition("=")
        if key.lower() == "expires":
            return parsedate_to_datetime(value)
    raise AssertionError(f"no Expires attribute in: {cookie_header}")


def _login(client, remember):
    data = {"username": SEEDED_USERNAME, "password": SEEDED_PASSWORD}
    if remember:
        data["remember_me"] = "y"
    return client.post("/auth/login", data=data)


def test_remember_me_is_ticked_by_default(client):
    """The checkbox should already be on, so the default is to stay signed in."""
    response = client.get("/auth/login")
    body = response.data.decode()

    # Match the input tag itself, not a window of surrounding text -- a loose
    # search picks up "checked" from a neighbouring element and passes when the
    # box is actually empty. WTForms emits attributes alphabetically, so
    # `checked` lands before `id`; do not assume an order.
    tags = re.findall(r"<input[^>]*\bid=\"remember_me\"[^>]*>", body)
    assert len(tags) == 1, f"expected exactly one remember_me input, found {len(tags)}"
    assert re.search(r"\bchecked\b", tags[0]), (
        f"the Keep me signed in box is not ticked by default: {tags[0]}"
    )


def test_login_with_remember_sets_a_long_lived_cookie(client):
    """This is the bug, expressed as an assertion.

    GUIConfig sets REMEMBER_COOKIE_DURATION to ten years. Anything short of a
    year here means the desktop app will start asking for a password again.
    """
    response = _login(client, remember=True)

    cookies = _set_cookie_headers(response, REMEMBER_COOKIE)
    assert cookies, "no remember_token cookie was set when Keep me signed in was ticked"

    expiry = _expiry_of(cookies[0])
    a_year_out = datetime.now(timezone.utc) + timedelta(days=365)
    assert expiry > a_year_out, f"remember_token expires {expiry}, sooner than a year from now"


def test_login_without_remember_sets_no_remember_cookie(client):
    """Unticking the box must still mean "forget me". Sign Out has to work."""
    response = _login(client, remember=False)
    assert not _set_cookie_headers(response, REMEMBER_COOKIE)


def test_signing_out_clears_the_remember_cookie(client):
    _login(client, remember=True)
    response = client.get("/auth/logout")

    cookies = _set_cookie_headers(response, REMEMBER_COOKIE)
    assert cookies, "logout did not touch the remember_token cookie"

    expiry = _expiry_of(cookies[0])
    assert expiry <= datetime.now(timezone.utc), "logout left the remember cookie alive"


def test_gui_config_cookies_work_over_plain_http(app):
    """127.0.0.1 is HTTP. A Secure cookie would never come back."""
    assert app.config["SESSION_COOKIE_SECURE"] is False
    assert app.config["REMEMBER_COOKIE_SECURE"] is False
    assert app.config["REMEMBER_COOKIE_DURATION"] >= timedelta(days=365)
