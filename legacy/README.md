# ProfitDjinn 1.x (legacy)

The original Flask + SQLAlchemy version, which ran in an Edge WebView2 window. Replaced by
the native 2.0 app at the repo root; kept here because:

- the 2.0 parity tests compare against it (`tools/Parity/` imports this code), and
- it is the rollback path: it opens the same `%LOCALAPPDATA%\ProfitDjinn\app.db`, including
  one that 2.0 has used.

Last release: `v1.0.0-beta`.

## Run it

```
python -m venv C:\Dev\venvs\ProfitDjinn
C:\Dev\venvs\ProfitDjinn\Scripts\pip install -r requirements-dev.txt
C:\Dev\venvs\ProfitDjinn\Scripts\python run_gui.py      # desktop window
C:\Dev\venvs\ProfitDjinn\Scripts\python -m pytest       # 33 tests
build.bat                                               # the 1.x EXE
```

Run these from this `legacy/` folder. Architecture notes for 1.x are in the root
`CLAUDE.md`, under "1.x reference".
