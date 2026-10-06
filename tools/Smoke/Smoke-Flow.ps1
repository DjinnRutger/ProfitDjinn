<#
  End-to-end smoke test of a built ProfitDjinn.exe, driven through UI Automation.

    .\tools\Smoke\Smoke-Flow.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Starts the app on an empty data folder (PROFITDJINN_DATA_DIR, never the real database),
  then does what a person would: create a customer, open their work order, log two hours
  of work, bill it, and record the payment. Then (2.4) it sets up a recurring invoice whose
  first date is two months back, confirms the three missed invoices, opens the next one from
  the customer page and issues it early. Then it closes the app and checks the database with
  Python's sqlite3: one paid invoice of $100.00, the work line billed onto it, one payment,
  four recurring invoices with their period text and run rows. 2.5: edits the recurring
  invoice (crashed in 2.4) to bill the period, gives an invoice line service dates, opens the
  PDF preview, and switches work orders off. 2.6: a new invoice for a customer added from the
  invoice form (Add Customer), and one picked by typing part of a name (real keystrokes: the
  window is brought to the front). Exits 1 on the first step that does not happen.
#>
param(
  # Also check the type-to-pick fill-in with real keystrokes. That needs the window in front,
  # so it takes focus for a few seconds; run it before a release, not on every change.
  [switch] $Keys,
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
# Off-screen and never focused, so a run does not take over the desktop (-Keys: see below).
if (-not $Keys) { $psi.EnvironmentVariables["PROFITDJINN_OFFSCREEN"] = "1" }
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
function Wait-Like([string] $pattern, [int] $seconds = 10) {
  for ($i = 0; $i -lt $seconds * 4; $i++) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.Name -like $pattern) { return $e } }
    Start-Sleep -Milliseconds 250
  }
  throw "Timed out waiting for text like '$pattern'."
}
function Invoke-Link([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Hyperlink)
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 400
}
function Toggle([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::CheckBox)
  $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Milliseconds 300
}
function Value-Of([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Edit)
  return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
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
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function Save-Screenshot([string] $path) {
  # The test window is off-screen, where Windows does not draw it. Move it on screen behind every
  # other window (no activation) so PrintWindow has something to capture.
  [SmokeWin]::SetWindowPos($proc.MainWindowHandle, [IntPtr]1, 0, 0, 0, 0, 0x0001 -bor 0x0010) | Out-Null   # HWND_BOTTOM; NOSIZE, NOACTIVATE
  Start-Sleep -Milliseconds 800
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
    # The window can exist before the form is in it; typing then lands nowhere. Wait for it.
    Wait-For "Save Customer" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Type-Into "Company / Name" "Smoke Test Co"
    Type-Into "Email" "billing@smoke.example"
    Click "Save Customer"
    Wait-For "Customer 'Smoke Test Co' created." | Out-Null
  }
  Step "open Work Orders with no open work" {
    # The empty state crashed 2.6.0 (InvalidCastException); demo data never reaches it.
    Click "Work Orders"
    Wait-For "No open work. Open a customer and start logging work so it doesn't get forgotten." | Out-Null
    Invoke-Link "Browse customers"
    Invoke-Link "Smoke Test Co"
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
  $start = (Get-Date).Date.AddMonths(-2)
  Step "set up a recurring invoice starting $($start.ToString('yyyy-MM-dd'))" {
    Invoke-Link "Smoke Test Co"
    Click "Recurring Invoice"
    Wait-For "Save Recurring Invoice" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Type-Into "First Invoice" $start.ToString('yyyy-MM-dd')
    Type-Into "On Day" ([string]$start.Day)
    Type-Into "Description" "Monthly support - {month} {year}"
    Type-Into "0.00" "75"
    Click "Save Recurring Invoice"
    Wait-For "Create invoices now?" | Out-Null
    Click "Create 3 Invoices"
    Wait-Like "Recurring invoice for Smoke Test Co saved. 3 recurring invoices created: INV1002 Smoke Test Co*" | Out-Null
  }
  Step "open the next one and issue it early" {
    Click "View the next invoice"
    Wait-For "Upcoming Invoice" | Out-Null
    Click "Issue Now"
    Wait-For "Issue this invoice now?" | Out-Null
    Click "Issue Invoice"
    Wait-Like "Invoice INV1005 created from the recurring invoice, dated*" | Out-Null
  }
  Step "give an invoice line service dates" {
    Click "Edit"
    Wait-For "Save Invoice" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Click "Service dates"
    Type-Into "Service from" "2026-09-01"
    Type-Into "Service to" "2026-09-30"
    Click "Save Invoice"
    Wait-For "Invoice INV1005 updated." | Out-Null
    Wait-For "Service: 09/01/26 - 09/30/26" | Out-Null
  }
  Step "preview the PDF without saving it" {
    Click "Preview"
    Wait-For "Save PDF" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Click "Close"
  }
  Step "edit the recurring invoice to bill the period" {
    Invoke-Link "Smoke Test Co"
    Click "Edit schedule"
    Wait-For "Save Recurring Invoice" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Click "Bill the period"
    Toggle "Show the period as service dates"
    Click "Save Recurring Invoice"
    Wait-For "Recurring invoice for Smoke Test Co updated." | Out-Null
  }
  Step "new invoice for a customer added from the invoice form" {
    Click "Invoices"
    Click "New Invoice"
    Wait-For "Save Invoice" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Type-Into "Customer" "Walk-in Client"
    Click "Add Customer"
    Wait-For "Company / Name" -type ([System.Windows.Automation.ControlType]::Edit) | Out-Null
    Type-Into "Email" "walkin@smoke.example"
    Click "Add Customer"          # the dialog's button (the last one found)
    Wait-For "Customer 'Walk-in Client' created." | Out-Null
    if ((Value-Of "Customer") -ne "Walk-in Client") { throw "The new customer was not filled in." }
    Click "Add Line"
    Type-Into "Description" "Drop-off repair"
    Type-Into "0.00" "25"
    Click "Save Invoice"
    Wait-For "Invoice INV1006 created." | Out-Null
  }
  Step "pick a customer by typing part of the name" {
    Click "Invoices"
    Click "New Invoice"
    Wait-For "Save Invoice" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    if ($Keys) {
      [SmokeWin]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
      (Wait-For "Customer" -type ([System.Windows.Automation.ControlType]::Edit)).SetFocus()
      Start-Sleep -Milliseconds 300
      Add-Type -AssemblyName System.Windows.Forms
      [System.Windows.Forms.SendKeys]::SendWait("Smo{TAB}")
      Start-Sleep -Milliseconds 500
      if ((Value-Of "Customer") -ne "Smoke Test Co") { throw "Typing 'Smo' did not fill in Smoke Test Co (got '$(Value-Of "Customer")')." }
    }
    else { Type-Into "Customer" "smoke test co" }   # an exact name in any case picks the record
    Click "Add Line"
    Type-Into "Description" "Quick fix"
    Type-Into "0.00" "10"
    Click "Save Invoice"
    Wait-For "Invoice INV1007 created." | Out-Null
  }
  Step "switch work orders off" {
    Click "Settings"
    Click "Features settings"
    Toggle "Work orders"
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Start-Sleep -Milliseconds 500
    if ((Find-All "Work Orders" ([System.Windows.Automation.ControlType]::Button)).Count -ne 0) { throw "Work Orders is still in the sidebar." }
  }
  Step "switch the Revenue report and Service items off" {
    Click "Features settings"
    Toggle "Revenue report"
    Toggle "Service items"
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Start-Sleep -Milliseconds 500
    foreach ($nav in "Revenue", "Items") {
      if ((Find-All $nav ([System.Windows.Automation.ControlType]::Button)).Count -ne 0) { throw "$nav is still in the sidebar." }
    }
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
inv = c.execute("select invoice_number, paid from invoices order by id").fetchall()
lines = c.execute("select description, status, invoice_id, amount from work_order_lines order by id").fetchall()
pays = c.execute("select amount, method from payments").fetchall()
total = c.execute("select sum(amount) from invoice_lines").fetchone()[0]
runs = c.execute("select count(*), count(invoice_id) from recurring_invoice_runs").fetchone()
monthly = c.execute("select description from invoice_lines where description like 'Monthly support - %' order by id").fetchall()
service = c.execute("select service_start, service_end from invoice_line_service").fetchall()
period = c.execute("select bill_period from recurring_invoice_lines").fetchall()
wo = c.execute("select value from settings where key = 'workorders_enabled'").fetchone()
switches = c.execute("select key, value from settings where key in ('revenue_enabled', 'items_enabled') order by key").fetchall()
if switches != [("items_enabled", "false"), ("revenue_enabled", "false")]: problems_switches = [f"revenue/items switches {switches}"]
else: problems_switches = []
problems = list(problems_switches)
if service != [("2026-09-01", "2026-09-30")]: problems.append(f"service dates {service}")
if period != [(1,)]: problems.append(f"bill period {period}")
if wo != ("false",): problems.append(f"work orders setting {wo}")
if inv != [("INV1001", 1), ("INV1002", 0), ("INV1003", 0), ("INV1004", 0), ("INV1005", 0), ("INV1006", 0), ("INV1007", 0)]: problems.append(f"invoices {inv}")
owners = c.execute("select i.invoice_number, c.name, c.email from invoices i join customers c on c.id = i.customer_id where i.invoice_number in ('INV1006', 'INV1007') order by 1").fetchall()
if owners != [("INV1006", "Walk-in Client", "walkin@smoke.example"), ("INV1007", "Smoke Test Co", "billing@smoke.example")]: problems.append(f"picked customers {owners}")
if runs != (4, 4): problems.append(f"recurring runs {runs}")
if len(monthly) != 4 or any("{" in d for (d,) in monthly): problems.append(f"recurring lines {monthly}")
if lines != [("Check backups", "pending", None, 0.0), ("Network setup", "billed", 1, 100.0)]: problems.append(f"work lines {lines}")
if pays != [(100.0, "cash")]: problems.append(f"payments {pays}")
if total != 435.0: problems.append(f"invoice total {total}")
print("; ".join(problems) if problems else "OK")
"@
$result = $check | python - (Join-Path $DataDir 'app.db')
if ($result -ne "OK") { Write-Host "FAILED: $result"; exit 1 }
Write-Host "ok"
Write-Host "Smoke test passed."
