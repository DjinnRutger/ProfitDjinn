# -*- mode: python ; coding: utf-8 -*-
#
# ProfitDjinn – PyInstaller spec file
# Build with:  pyinstaller profitdjinn.spec --clean --noconfirm
#
# Output: dist/ProfitDjinn/ProfitDjinn.exe  (onedir bundle)
#

import os

from PyInstaller.utils.hooks import collect_data_files, collect_submodules

# dist_icon.ico is committed, so this normally exists. build.bat can regenerate
# it from Images-Org/, but that folder is gitignored and absent in a fresh
# clone. Falling back to no icon here means the build never fails over artwork,
# and build.bat no longer has to rewrite this file mid-build.
_ICON = "dist_icon.ico" if os.path.exists("dist_icon.ico") else None

block_cipher = None

# ── Data files (non-Python assets that must be bundled) ──────────────────────
datas = [
    # Flask templates & static assets
    ("app/templates",  "app/templates"),
    ("app/static",     "app/static"),
    # fpdf2 bundles its own fonts/graphics — collect them all
    *collect_data_files("fpdf"),
    # alembic env templates (needed by Flask-Migrate at runtime)
    *collect_data_files("alembic"),
    # pywebview: injected JS (webview/js/) plus the WebView2 and WinForms
    # interop DLLs under webview/lib/. Without these the window opens blank.
    *collect_data_files("webview"),
    # pythonnet's native loader (clr_loader/ffi/dlls/). pywebview reaches
    # WebView2 through .NET, so this is not optional on Windows.
    *collect_data_files("clr_loader"),
    *collect_data_files("pythonnet"),
]

# ── Hidden imports (packages PyInstaller can't auto-detect) ──────────────────
hiddenimports = [
    # Flask ecosystem
    "flask_login",
    "flask_wtf",
    "flask_wtf.csrf",
    "flask_sqlalchemy",
    "flask_migrate",
    "flask_limiter",
    "flask_limiter.storage",
    "flask_talisman",
    # Crypto
    "argon2",
    "argon2._utils",
    "argon2.profiles",
    "argon2._password_hasher",
    "cffi",
    "_cffi_backend",
    # Validation
    "email_validator",
    "email_validator.syntax",
    "email_validator.deliverability",
    # SQLAlchemy dialects
    "sqlalchemy.dialects.sqlite",
    "sqlalchemy.pool",
    "sqlalchemy.ext.declarative",
    # PDF
    "fpdf",
    "fpdf.enums",
    "fpdf.fonts",
    "fpdf.output",
    # Desktop shell: pywebview on Windows goes through WinForms -> WebView2,
    # and reaches .NET via pythonnet. PyInstaller finds none of this on its
    # own because the platform backend is chosen at runtime.
    "webview",
    "webview.platforms.winforms",
    "webview.platforms.edgechromium",
    "webview.http",
    "clr",
    "clr_loader",
    "pythonnet",
    "bottle",
    "proxy_tools",
    # Data locations. Imported by run_gui.py before the app package.
    "paths",
    # App blueprints (PyInstaller can't walk dynamic registrations)
    "app.blueprints.auth",
    "app.blueprints.main",
    "app.blueprints.admin",
    "app.blueprints.database_mgr",
    "app.blueprints.customers",
    "app.blueprints.invoices",
    "app.blueprints.items",
    "app.blueprints.work_orders",
    # App models
    "app.models",
    "app.models.user",
    "app.models.role",
    "app.models.permission",
    "app.models.setting",
    "app.models.audit",
    "app.models.customer",
    "app.models.invoice",
    "app.models.service_item",
    "app.models.payment",
    "app.models.work_order",
    # App forms
    "app.forms.admin",
    "app.forms.auth",
    "app.forms.profile",
    "app.forms.customer_form",
    "app.forms.invoice_form",
    "app.forms.item_form",
    "app.forms.work_order_form",
    # App utils
    "app.utils.helpers",
    "app.utils.settings",
    "app.utils.pdf_generator",
    "app.utils.decorators",
    # Config & extensions
    "app.config",
    "app.extensions",
]

a = Analysis(
    ["run_gui.py"],
    pathex=["."],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        # Things we definitely don't need
        "tkinter",
        "matplotlib",
        "numpy",
        "pandas",
        "scipy",
        "pytest",
        "IPython",
        "notebook",
        # pywebview backends for other platforms. Bundling them adds weight
        # and drags in Qt/GTK that are not installed anyway.
        "webview.platforms.gtk",
        "webview.platforms.qt",
        "webview.platforms.cocoa",
        "webview.platforms.cef",
        "webview.platforms.android",
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="ProfitDjinn",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    # UPX is left off deliberately: it can corrupt the .NET assemblies that
    # pywebview loads, and a broken WebView2 DLL fails at window-open time
    # rather than at build time, which is a miserable thing to debug.
    upx=False,
    console=False,          # No black console window
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=_ICON,             # See _ICON above; None if the file is missing
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name="ProfitDjinn",
)
