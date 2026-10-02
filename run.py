"""
ProfitDjinn – entry point.
Run with:  flask run   (or python run.py for direct execution)
"""
import os
from app import create_app

app = create_app(os.environ.get("FLASK_ENV", "development"))

if __name__ == "__main__":
    # Loopback only: debug mode exposes the Werkzeug console, and a fresh install
    # serves the first-start setup page to whoever reaches it first.
    app.run(host="127.0.0.1", port=5000, debug=True)
