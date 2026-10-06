<#
  Starts ProfitDjinn against a throwaway data folder and saves a screenshot of its window.

    .\tools\Smoke\Capture.ps1 -Exe <ProfitDjinn.exe> -DataDir <folder> -Out shot.png [-Page gallery] [-Keys "..."] [-Wait 4] [-Leave]

  -DataDir is handed to the app as PROFITDJINN_DATA_DIR, so it reads <DataDir>\app.db and never
  touches the real database. (.NET ignores a changed LOCALAPPDATA, so that variable is no
  protection; found the hard way on 2026-10-02.) The script stops the app at once if its title
  does not show "[test data]". -Keys are SendKeys strings sent once the window is up (e.g. "^+g"
  opens the style gallery). -Leave keeps the app running for further captures.
  Without -Keys the app runs off-screen and never takes focus, and draws its own window to the
  PNG (PROFITDJINN_SNAPSHOT): nothing appears on the desktop. With -Keys the window has to be
  on screen and in front for the keystrokes, and it is captured with PrintWindow.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [Parameter(Mandatory)] [string] $DataDir,
  [Parameter(Mandatory)] [string] $Out,
  [string] $Keys = "",
  [string] $Page = "",
  [int] $Wait = 4,
  [int] $Width = 1440,
  [int] $Height = 900,
  [switch] $Leave
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$DataDir = [System.IO.Path]::GetFullPath($DataDir)
$real = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProfitDjinn'
if ($DataDir.TrimEnd('\') -ieq $real) { throw "Refusing to run against the real data folder $real." }
if (-not (Test-Path (Join-Path $DataDir 'app.db'))) { Write-Warning "No app.db in $DataDir; the app will create a fresh one there." }

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
if ($Page) { $psi.Arguments = "--page $Page" }
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["PROFITDJINN_DATA_DIR"] = $DataDir
if (-not $Keys) {
  # Off-screen, never focused; the app saves the PNG itself after -Wait seconds.
  $psi.EnvironmentVariables["PROFITDJINN_OFFSCREEN"] = "1"
  $psi.EnvironmentVariables["PROFITDJINN_SNAPSHOT"] = [System.IO.Path]::GetFullPath($Out)
  $psi.EnvironmentVariables["PROFITDJINN_SNAPSHOT_DELAY"] = "$Wait"
  Remove-Item $Out -ErrorAction SilentlyContinue
}
$p = [System.Diagnostics.Process]::Start($psi)
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
if ($p.MainWindowHandle -eq 0) { throw "ProfitDjinn did not open a window." }
if ($p.MainWindowTitle -notlike '*test data*') { $p.Kill(); throw "The app did not pick up PROFITDJINN_DATA_DIR (title: $($p.MainWindowTitle)). Stopped it before it could do anything." }
$h = $p.MainWindowHandle
if (-not $Keys) {
  for ($i = 0; $i -lt ($Wait + 30) * 4 -and -not (Test-Path $Out); $i++) { Start-Sleep -Milliseconds 250 }
  if (-not (Test-Path $Out)) { $p.Kill(); throw "The app did not save $Out." }
  if (-not $Leave) { $p.Kill() | Out-Null }
  "saved $Out (pid $($p.Id))"
  return
}
[Win]::MoveWindow($h, 20, 20, $Width, $Height, $true) | Out-Null
Start-Sleep -Seconds $Wait
if ($Keys) {
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait($Keys)
  Start-Sleep -Seconds 2
}
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win]::PrintWindow($h, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if (-not $Leave) { $p.Kill() | Out-Null }
"saved $Out (pid $($p.Id))"
