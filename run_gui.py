"""ProfitDjinn - Desktop app entry point.

    From source:  python run_gui.py
    Frozen EXE:   ProfitDjinn.exe

Opens the Flask app in an Edge WebView2 window: a real Windows window with
no address bar, no tabs, and no Chrome process behind it.

Why the environment variables are set before the app import: app/config.py
reads SECRET_KEY and DATABASE_URI out of os.environ while its class bodies
are evaluated, which happens the moment `app` is first imported. Setting
them afterwards would be too late and silently ignored. Keep this order.
"""
import os

import paths

os.environ["DATABASE_URI"] = paths.database_uri()
os.environ["SECRET_KEY"] = paths.load_or_create_secret_key()
os.environ["FLASK_INSTANCE_PATH"] = str(paths.data_dir())

from app import create_app  # noqa: E402  (must follow the environment setup above)

app = create_app("gui")


def main() -> None:
    import webview

    with app.app_context():
        from app.utils.settings import get_setting
        title = get_setting("app_name", paths.APP_NAME)

    webview.create_window(
        title,
        app,
        width=1440,
        height=900,
        min_size=(1024, 700),
    )

    # private_mode defaults to True, which throws away cookies and local
    # storage when the window closes. That is what made "Keep me signed in"
    # useless in the old flaskwebgui build. Turning it off and pinning a
    # storage_path is the fix.
    webview.start(
        private_mode=False,
        storage_path=str(paths.webview_profile_dir()),
    )


if __name__ == "__main__":
    main()
