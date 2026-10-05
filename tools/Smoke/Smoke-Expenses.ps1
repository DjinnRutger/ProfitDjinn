<#
  End-to-end smoke test of the 2.2 Expenses feature in a built ProfitDjinn.exe, driven
  through UI Automation.

    .\tools\Smoke\Smoke-Expenses.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Starts the app on an empty data folder (PROFITDJINN_DATA_DIR, never the real database)
  and does what a person would: turn Expenses on in Settings, add a vendor, add a $100
  expense, pay $40 of it, set up a monthly auto-paid recurring expense that started two
  months ago (which back-fills three expenses after a confirmation), then turn Expenses off
  and check Vendors and Expenses leave the sidebar. Then it checks the database with
  Python's sqlite3. Exits 1 on the first step that does not happen.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir = (Join-Path $env:TEMP ("profitdjinn-smoke-exp-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$inv = [System.Globalization.CultureInfo]::InvariantCulture

$real = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProfitDjinn'
$DataDir = [System.IO.Path]::GetFullPath($DataDir)
if ($DataDir.TrimEnd('\') -ieq $real) { throw "Refusing to run against the real data folder." }
New-Item -ItemType Directory -Force $DataDir | Out-Null
if (Test-Path (Join-Path $DataDir 'app.db')) { throw "$DataDir already has an app.db. Give an empty folder." }

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.Arguments = "--page settings:features"
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["PROFITDJINN_DATA_DIR"] = $DataDir
$proc = [System.Diagnostics.Process]::Start($psi)
for ($i = 0; $i -lt 80 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $proc.Refresh() }
if ($proc.MainWindowHandle -eq 0) { throw "No window." }
if ($proc.MainWindowTitle -notlike '*test data*') { $proc.Kill(); throw "App ignored PROFITDJINN_DATA_DIR." }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

function Find-All([string] $name, $type = $null, $under = $root) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ($AE::NameProperty), $name
  if ($type) {
    $typeCond = New-Object System.Windows.Automation.PropertyCondition ($AE::ControlTypeProperty), $type
    $cond = New-Object System.Windows.Automation.AndCondition $cond, $typeCond
  }
  return $under.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
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
  $e = Wait-For $name -type $CT::Button
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 400
}
function Type-Into([string] $name, [string] $text) {
  $e = Wait-For $name -type $CT::Edit
  $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}
function Toggle([string] $name) {
  $e = Wait-For $name -type $CT::CheckBox
  $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Milliseconds 300
}
function Pick([string] $combo, [string] $item) {
  $c = Wait-For $combo -type $CT::ComboBox
  $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 400
  $items = Find-All $item $CT::ListItem $c
  if ($items.Count -eq 0) { throw "No '$item' in '$combo'." }
  $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Start-Sleep -Milliseconds 300
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
public static class SmokeWinExp {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function Save-Screenshot([string] $path) {
  $r = New-Object SmokeWinExp+RECT
  [SmokeWinExp]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); [SmokeWinExp]::PrintWindow($proc.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

# The recurring expense starts on the 1st, two months ago: three dates through today.
$today = [DateTime]::Today
$start = (New-Object DateTime $today.Year, $today.Month, 1).AddMonths(-2)
$dates = 0..2 | ForEach-Object { $start.AddMonths($_) }
$madeNotice = "3 recurring expenses added: " + (($dates | ForEach-Object { "Smoke rent ($($_.ToString('MMM dd', $inv)))" }) -join ", ") + "."

try {
  Write-Host "Smoke test on $DataDir"
  Step "Expenses starts off" {
    # Wait for the page first, so "no Vendors button" means hidden, not "not drawn yet".
    Wait-For "Track expenses" -type $CT::CheckBox | Out-Null
    Start-Sleep -Milliseconds 500
    if ((Find-All "Vendors" $CT::Button).Count -ne 0) { throw "Vendors is in the sidebar before Expenses is turned on." }
  }
  Step "turn Expenses on" {
    Toggle "Track expenses"
    Click "Save Changes"
    Wait-For "Settings saved. Expenses is on: Vendors, Expenses and Profit & Loss are now in the sidebar." | Out-Null
  }
  Step "add a vendor" {
    Click "Vendors"
    Click "New Vendor"
    Type-Into "Vendor Name" "Smoke Supply"
    Pick "Default Category" "Supplies"
    Click "Save Vendor"
    Wait-For "Vendor 'Smoke Supply' created." | Out-Null
  }
  Step "add a `$100 expense" {
    Click "New Expense"
    Type-Into "Description" "Smoke paper"
    Type-Into "Amount" "100"
    Click "Save Expense"
    Wait-For "Expense 'Smoke paper' saved." | Out-Null
  }
  Step "pay `$40 of it" {
    Click "Record Payment"
    Wait-For "Payment Method" | Out-Null
    Type-Into "Amount" "40"
    Click "Record Payment"       # the dialog's button (the last one found)
    Wait-For "Partial payment of `$40.00 recorded. Balance remaining: `$60.00." | Out-Null
  }
  # 2.6: the vendor box is type-to-pick. Real keystrokes for the fill-in (window to the front).
  Step "pick the vendor by typing part of its name" {
    Click "Expenses"
    Click "New Expense"
    Wait-For "Save Expense" -type $CT::Button | Out-Null
    Start-Sleep -Milliseconds 500
    [SmokeWinExp]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    (Wait-For "Vendor" -type $CT::Edit).SetFocus()
    Start-Sleep -Milliseconds 300
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait("Smo{TAB}")
    Start-Sleep -Milliseconds 500
    $got = (Wait-For "Vendor" -type $CT::Edit).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($got -ne "Smoke Supply") { throw "Typing 'Smo' did not fill in Smoke Supply (got '$got')." }
    Type-Into "Description" "Smoke tape"
    Type-Into "Amount" "12"
    Click "Save Expense"
    Wait-For "Expense 'Smoke tape' saved." | Out-Null
  }
  Step "add a vendor from the expense form" {
    Click "Expenses"
    Click "New Expense"
    Wait-For "Save Expense" -type $CT::Button | Out-Null
    Start-Sleep -Milliseconds 500
    Type-Into "Vendor" "Fresh Vendor"
    Click "Add Vendor"
    Wait-For "Vendor Name" -type $CT::Edit | Out-Null
    Pick "Default Category" "Advertising"
    Click "Add Vendor"            # the dialog's button (the last one found)
    Wait-For "Vendor 'Fresh Vendor' created." | Out-Null
    Type-Into "Description" "Flyers"
    Type-Into "Amount" "30"
    Click "Save Expense"
    Wait-For "Expense 'Flyers' saved." | Out-Null
  }
  Step "set up a recurring expense that back-fills three" {
    Click "Expenses"
    Click "Recurring"
    Click "New Recurring Expense"
    Pick "Category" "Rent/Lease"
    Type-Into "Description" "Smoke rent"
    Type-Into "Amount" "500"
    Type-Into "Starting" $start.ToString("yyyy-MM-dd", $inv)
    Type-Into "On Day" "1"
    Click "Save Recurring Expense"
    Click "Add 3 Expenses"
    Wait-For ("Recurring expense 'Smoke rent' saved. " + $madeNotice) | Out-Null
  }
  Step "the P&L shows the cash spent" {
    Click "Profit & Loss"
    Wait-For "P&L CSV" -type $CT::Button | Out-Null
    # Cash out this year: the $40 payment plus each $500 rent dated this year. No income.
    $spent = 40 + 500 * @($dates | Where-Object { $_.Year -eq $today.Year }).Count
    Wait-For ("-`$" + $spent.ToString("0.00", $inv)) | Out-Null
  }
  Step "turn Expenses off" {
    Click "Settings"
    Click "Features settings"
    Toggle "Track expenses"
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Start-Sleep -Milliseconds 500
    if ((Find-All "Vendors" $CT::Button).Count -ne 0) { throw "Vendors is still in the sidebar." }
    if ((Find-All "Expenses" $CT::Button).Count -ne 0) { throw "Expenses is still in the sidebar." }
    if ((Find-All "Profit & Loss" $CT::Button).Count -ne 0) { throw "Profit & Loss is still in the sidebar." }
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
problems = []
on = c.execute("select value from settings where key='expenses_enabled'").fetchone()
if on != ("false",): problems.append(f"expenses_enabled {on}")
v = c.execute("select v.name, k.name from vendors v join expense_categories k on k.id = v.default_category_id").fetchall()
if sorted(v) != [("Fresh Vendor", "Advertising"), ("Smoke Supply", "Supplies")]: problems.append(f"vendors {v}")
picked = c.execute("select e.description, v.name, k.name from expenses e join vendors v on v.id = e.vendor_id join expense_categories k on k.id = e.category_id where e.description in ('Smoke tape', 'Flyers') order by 1").fetchall()
if picked != [("Flyers", "Fresh Vendor", "Advertising"), ("Smoke tape", "Smoke Supply", "Supplies")]: problems.append(f"picked vendors {picked}")
paper = c.execute("select e.amount, k.name, (select sum(amount) from expense_payments p where p.expense_id = e.id) from expenses e join expense_categories k on k.id = e.category_id where e.description = 'Smoke paper'").fetchall()
if paper != [(100.0, "Supplies", 40.0)]: problems.append(f"paper {paper}")
rent = c.execute("select e.date, e.amount, p.amount, p.method from expenses e join expense_payments p on p.expense_id = e.id where e.recurring_id is not null order by e.date").fetchall()
want = [(d, 500.0, 500.0, "credit_card") for d in sys.argv[2].split(",")]
if rent != want: problems.append(f"rent {rent} != {want}")
print("; ".join(problems) if problems else "OK")
"@
$result = $check | python - (Join-Path $DataDir 'app.db') (($dates | ForEach-Object { $_.ToString('yyyy-MM-dd', $inv) }) -join ",")
if ($result -ne "OK") { Write-Host "FAILED: $result"; exit 1 }
Write-Host "ok"
Write-Host "Expenses smoke test passed."
