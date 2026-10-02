@echo off
setlocal enabledelayedexpansion
title ProfitDjinn Build

REM ---------------------------------------------------------------------------
REM ASCII only, on purpose. This file used to contain box-drawing characters;
REM cmd.exe reads .bat files in the console's OEM codepage, mangled the UTF-8
REM bytes, and broke the parsing of the lines that followed. Keep it plain.
REM ---------------------------------------------------------------------------

REM The venv and all build output live outside the project so OneDrive never
REM syncs them. Same paths on both machines.
set "VENV=C:\Dev\venvs\ProfitDjinn"
set "OUT=C:\Dev\ProfitDjinn\dist"
set "WORK=C:\Dev\ProfitDjinn\build"

echo.
echo  ============================================
echo   ProfitDjinn ^| Windows EXE Build Script
echo  ============================================
echo.

REM --- Activate virtualenv ---------------------------------------------------
if not exist "%VENV%\Scripts\activate.bat" (
    echo ERROR: virtual environment not found at %VENV%
    echo.
    echo Create it with:
    echo     python -m venv %VENV%
    echo     %VENV%\Scripts\pip install -r requirements-dev.txt
    echo.
    pause
    exit /b 1
)
call "%VENV%\Scripts\activate.bat"
echo [OK] Virtual environment activated: %VENV%

REM --- Install / upgrade build tools -----------------------------------------
echo.
echo [1/5] Installing build dependencies...
pip install -r requirements-dev.txt --quiet --upgrade
if errorlevel 1 (
    echo ERROR: pip install failed. Check your internet connection.
    pause
    exit /b 1
)
echo [OK] Build dependencies ready.

REM --- Refresh the icon from source art (optional) ---------------------------
REM dist_icon.ico is committed, so this step only refreshes it. Images-Org is
REM gitignored and missing in a fresh clone; the spec falls back to no icon.
echo.
echo [2/5] Refreshing application icon...
if exist "Images-Org\ProfitDjinn-No Name-Md.png" (
    python -c "from PIL import Image; sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)]; img=Image.open('Images-Org/ProfitDjinn-No Name-Md.png').convert('RGBA'); imgs=[img.resize(s,Image.LANCZOS) for s in sizes]; imgs[0].save('dist_icon.ico',format='ICO',sizes=sizes,append_images=imgs[1:])"
    if errorlevel 1 (
        echo [WARN] Icon conversion failed. Using the committed dist_icon.ico.
    ) else (
        echo [OK] dist_icon.ico regenerated.
    )
) else (
    echo [SKIP] Images-Org not present. Using the committed dist_icon.ico.
)

REM --- Clean previous build --------------------------------------------------
echo.
echo [3/5] Cleaning previous build artifacts...
if exist "%OUT%\ProfitDjinn" rmdir /s /q "%OUT%\ProfitDjinn"
if exist "%WORK%"            rmdir /s /q "%WORK%"
echo [OK] Clean.

REM --- Run PyInstaller -------------------------------------------------------
echo.
echo [4/5] Running PyInstaller (this takes 1-3 minutes)...
pyinstaller profitdjinn.spec --clean --noconfirm --distpath "%OUT%" --workpath "%WORK%"
if errorlevel 1 (
    echo.
    echo ERROR: PyInstaller failed. See the output above.
    echo Common fixes:
    echo   - Add the missing import to hiddenimports in profitdjinn.spec
    echo   - Re-run with: pyinstaller profitdjinn.spec --clean --noconfirm --log-level DEBUG
    echo.
    pause
    exit /b 1
)

REM --- Drop the intermediate work folder -------------------------------------
rmdir /s /q "%WORK%" 2>nul

REM --- Summary ---------------------------------------------------------------
echo.
echo [5/5] Build complete.
echo.
echo   Output folder : %OUT%\ProfitDjinn\
echo   Run the app   : %OUT%\ProfitDjinn\ProfitDjinn.exe
echo.
echo   Your database, secret key, and sign-in cookies live in
echo   %%LOCALAPPDATA%%\ProfitDjinn\ - NOT next to the EXE.
echo   Deleting and rebuilding the output folder loses no data.
echo.
echo   To distribute: zip the entire %OUT%\ProfitDjinn\ folder.
echo.
pause
