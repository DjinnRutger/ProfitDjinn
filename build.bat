@echo off
setlocal enabledelayedexpansion
title ProfitDjinn Build

echo.
echo  ============================================
echo   ProfitDjinn ^| Windows EXE Build Script
echo  ============================================
echo.

REM ── Activate virtualenv ──────────────────────────────────────────────────────
if not exist ".venv\Scripts\activate.bat" (
    echo ERROR: .venv not found. Run:  python -m venv .venv
    echo        Then:               .venv\Scripts\pip install -r requirements.txt
    pause & exit /b 1
)
call .venv\Scripts\activate.bat
echo [OK] Virtual environment activated.

REM ── Install / upgrade build tools ────────────────────────────────────────────
echo.
echo [1/5] Installing build dependencies...
pip install flaskwebgui pyinstaller pillow --quiet --upgrade
if errorlevel 1 (
    echo ERROR: pip install failed. Check your internet connection.
    pause & exit /b 1
)
echo [OK] Build dependencies ready.

REM ── Convert PNG icon to ICO ───────────────────────────────────────────────────
echo.
echo [2/5] Converting icon PNG to ICO...
python -c ^
  "from PIL import Image; ^
   sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)]; ^
   img=Image.open('Images-Org/ProfitDjinn-No Name-Md.png').convert('RGBA'); ^
   imgs=[img.resize(s,Image.LANCZOS) for s in sizes]; ^
   imgs[0].save('dist_icon.ico',format='ICO',sizes=sizes,append_images=imgs[1:]); ^
   print('  Saved dist_icon.ico')"
if errorlevel 1 (
    echo WARNING: Icon conversion failed - building without custom icon.
    REM Patch spec to remove icon reference so build still works
    python -c "s=open('profitdjinn.spec').read(); open('profitdjinn.spec','w').write(s.replace('icon=\"dist_icon.ico\"','icon=None'))"
)

REM ── Clean previous build ─────────────────────────────────────────────────────
echo.
echo [3/5] Cleaning previous build artifacts...
if exist "dist\ProfitDjinn" rmdir /s /q "dist\ProfitDjinn"
if exist "build"            rmdir /s /q "build"
echo [OK] Clean.

REM ── Run PyInstaller ───────────────────────────────────────────────────────────
echo.
echo [4/5] Running PyInstaller (this takes 1-3 minutes)...
pyinstaller profitdjinn.spec --clean --noconfirm
if errorlevel 1 (
    echo.
    echo ERROR: PyInstaller failed. See output above for details.
    echo Common fixes:
    echo   - Add missing import to hiddenimports in profitdjinn.spec
    echo   - Run:  pyinstaller profitdjinn.spec --clean --noconfirm --log-level DEBUG
    pause & exit /b 1
)

REM ── Restore spec if we patched icon ──────────────────────────────────────────
python -c "s=open('profitdjinn.spec').read(); open('profitdjinn.spec','w').write(s.replace('icon=None','icon=\"dist_icon.ico\"'))" 2>nul

REM ── Remove build folder (no longer needed) ────────────────────────────────────
rmdir /s /q "build" 2>nul

REM ── Final summary ─────────────────────────────────────────────────────────────
echo.
echo [5/5] Build complete!
echo.
echo  Output folder : dist\ProfitDjinn\
echo  Run the app   : dist\ProfitDjinn\ProfitDjinn.exe
echo.
echo  NOTE: The 'instance\' folder (database) will be created next to
echo        ProfitDjinn.exe on first launch.  Keep it with the EXE.
echo.
echo  To distribute: zip the entire dist\ProfitDjinn\ folder.
echo.
pause
