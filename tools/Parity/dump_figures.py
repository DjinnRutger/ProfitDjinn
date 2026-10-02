"""Copy a real ProfitDjinn database and record what 1.x computes from it.

    python tools\\Parity\\dump_figures.py [source.db] [out-dir]

source.db defaults to %LOCALAPPDATA%\\ProfitDjinn\\app.db and out-dir to
C:\\Dev\\ProfitDjinn\\parity. The source is only ever read: it is copied with SQLite's
backup API, and 1.x then works on the copy. The out-dir gets:

    real.db              the copy (1.x upgrades it in place, as it would at start)
    real_expected.json   every figure 1.x computes from it

Then run the 2.0 side against the same folder:

    set PROFITDJINN_PARITY_DIR=C:\\Dev\\ProfitDjinn\\parity
    dotnet test tests\\ProfitDjinn.Tests --filter RealDatabase

Real data. The out-dir is outside OneDrive and outside the repo. Keep it there.
"""
import json
import os
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(os.environ["LOCALAPPDATA"]) / "ProfitDjinn" / "app.db"
out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(r"C:\Dev\ProfitDjinn\parity")

if not source.is_file():
    raise SystemExit(f"No database at {source}")
if ROOT in out_dir.resolve().parents or "OneDrive" in str(out_dir.resolve()):
    raise SystemExit(f"Refusing to write real data inside the repo or OneDrive: {out_dir}")
out_dir.mkdir(parents=True, exist_ok=True)
copy = out_dir / "real.db"
if copy.exists():
    copy.unlink()

src = sqlite3.connect(f"file:{source}?mode=ro", uri=True)
dst = sqlite3.connect(copy)
src.backup(dst)
src.close()
dst.close()

os.environ["DATABASE_URI"] = "sqlite:///" + copy.as_posix()
os.environ["SECRET_KEY"] = "parity-dump-only"
os.environ["FLASK_INSTANCE_PATH"] = str(out_dir)
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from app import create_app  # noqa: E402
from app.extensions import db  # noqa: E402
from figures import figures  # noqa: E402

app = create_app("gui")
with app.app_context():
    result = figures()
    db.engine.dispose()

(out_dir / "real_expected.json").write_text(json.dumps(result, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Copied {source} to {copy}")
print(f"Wrote {out_dir / 'real_expected.json'}: {len(result['invoices'])} invoices, "
      f"{len(result['customers'])} customers, {len(result['work_orders'])} work orders")
