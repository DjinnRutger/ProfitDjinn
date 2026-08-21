"""paths.py decides where the database lives. Getting it wrong loses data."""
import paths


def test_dev_mode_uses_the_project_instance_folder():
    assert not paths.is_frozen()
    assert paths.data_dir().name == "instance"


def test_database_uri_uses_forward_slashes():
    """SQLAlchemy will not parse a Windows path with backslashes."""
    uri = paths.database_uri()
    assert uri.startswith("sqlite:///")
    assert "\\" not in uri


def test_secret_key_is_stable_across_calls():
    """A changing key silently invalidates every remember cookie."""
    assert paths.load_or_create_secret_key() == paths.load_or_create_secret_key()
    assert len(paths.load_or_create_secret_key()) == 64


def test_webview_profile_lives_under_the_data_dir():
    assert paths.webview_profile_dir().parent == paths.data_dir()
