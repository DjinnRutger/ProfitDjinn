<#
  End-to-end smoke test of a built ProfitDjinn.exe, driven through UI Automation.

    .\tools\Smoke\Smoke-Flow.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Starts the app on an empty data folder (PROFITDJINN_DATA_DIR, never the real database),
  then does what a person would: create a customer, open their work order, log two hours
  of work, bill it, and record the payment. Then it closes the app and checks the database
  with Python's sqlite3: one paid invoice of $100.00, the work line billed onto it, one
  payment. Exits 1 on the first step that does not happen.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir = (Join-Path $env:TEMP ("profitdjinn-smoke-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$real = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProfitDjinn'
$DataDir = [System.IO.Path]::GetFullPath($DataDir)
if ($DataDir.TrimEnd('\') -ieq $real) { throw "Refusing to run against the real data folder." }
New-Item -ItemType Directory -Force $DataDir | Out-Null
if (Test-Path (Join-Path $DataDir 'app.db')) { throw "$DataDir already has an app.db. Give an empty folder." }

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.Arguments = "--page newcustomer"
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["PROFITDJINN_DATA_DIR"] = $DataDir
$proc = [System.Diagnostics.Process]::Start($psi)
for ($i = 0; $i -lt 80 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $proc.Refresh() }
if ($proc.MainWindowHandle -eq 0) { throw "No window." }
if ($proc.MainWindowTitle -notlike '*test data*') { $proc.Kill(); throw "App ignored PROFITDJINN_DATA_DIR." }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

function Find-All([string] $name, $type = $null) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
  if ($type) {
    $typeCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), $type
    $cond = New-Object System.Windows.Automation.AndCondition $cond, $typeCond
  }
  return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Wait-For([string] $name, [int] $seconds = 10, $type = $null) {
  for ($i = 0; $i -lt $seconds * 4; $i++) {
    $found = Find-All $name $type
    if ($found.Count -gt 0) { return $found[$found.Count - 1] }
    Start-Sleep -Milliseconds 250
  }
  throw "Timed out waiting for '$name'."
}
function Click([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Button)
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 400
}
function Type-Into([string] $name, [string] $text) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Edit)
  $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}
function Step([string] $what, [scriptblock] $do) {
  Write-Host -NoNewline "  $what ... "
  & $do
  Write-Host "ok"
}

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SmokeWin {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function Save-Screenshot([string] $path) {
  $r = New-Object SmokeWin+RECT
  [SmokeWin]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); [SmokeWin]::PrintWindow($proc.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

try {
  Write-Host "Smoke test on $DataDir"
  Step "create a customer" {
    Type-Into "Company / Name" "Smoke Test Co"
    Type-Into "Email" "billing@smoke.example"
    Click "Save Customer"
    Wait-For "Customer 'Smoke Test Co' created." | Out-Null
  }
  Step "open the work order tab" {
    Click "Work Order"
    Wait-For "Add To-Do" | Out-Null
  }
  Step "add a to-do" {
    Type-Into "What needs doing? (e.g. Configure VLANs)" "Check backups"
    Click "Add To-Do"
    Wait-For "To-do added." | Out-Null
  }
  Step "log two hours at `$50" {
    Click "Log Work$([char]0x2026)"
    Type-Into "Description" "Network setup"
    Type-Into "Hours" "2"
    Type-Into "Rate (`$/hr)" "50"
    Click "Log Work"
    Wait-For "Logged work: Network setup" | Out-Null
  }
  Step "bill it" {
    Click "Bill `$100.00"
    Wait-For "Create Invoice" | Out-Null
    Click "Create Invoice"
    Wait-For "Invoice INV1001 created from 1 work order line." | Out-Null
  }
  Step "record the payment" {
    Click "Record Payment"
    Wait-For "Payment Method" | Out-Null
    Click "Record Payment"       # the dialog's button (the last one found)
    Wait-For "Payment of `$100.00 recorded. Invoice paid in full." | Out-Null
  }
}
catch {
  $shot = Join-Path $DataDir 'failure.png'
  Save-Screenshot $shot
  Write-Host ""
  Write-Host "FAILED: $($_.Exception.Message)"
  Write-Host "Screenshot: $shot"
  exit 1
}
finally {
  if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $proc.HasExited) { $proc.Kill() } }
}

Write-Host -NoNewline "  check the database ... "
$check = @"
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
inv = c.execute("select invoice_number, paid from invoices").fetchall()
lines = c.execute("select description, status, invoice_id, amount from work_order_lines order by id").fetchall()
pays = c.execute("select amount, method from payments").fetchall()
total = c.execute("select sum(amount) from invoice_lines").fetchone()[0]
problems = []
if inv != [("INV1001", 1)]: problems.append(f"invoices {inv}")
if lines != [("Check backups", "pending", None, 0.0), ("Network setup", "billed", 1, 100.0)]: problems.append(f"work lines {lines}")
if pays != [(100.0, "cash")]: problems.append(f"payments {pays}")
if total != 100.0: problems.append(f"invoice total {total}")
print("; ".join(problems) if problems else "OK")
"@
$result = $check | python - (Join-Path $DataDir 'app.db')
if ($result -ne "OK") { Write-Host "FAILED: $result"; exit 1 }
Write-Host "ok"
Write-Host "Smoke test passed."
