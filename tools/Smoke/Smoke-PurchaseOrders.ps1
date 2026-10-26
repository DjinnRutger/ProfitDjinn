<#
  2.9. Purchase orders end to end, driven through UI Automation on a throwaway data folder.

    .\tools\Smoke\Smoke-PurchaseOrders.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Turns Purchase orders on in Settings > Features (which must turn Vendors on and hold it on),
  sets the business letterhead and a PO prefix, creates a PO for a vendor added from the PO form
  with its address, gives it two lines, previews the PDF, marks it received, finds it on the
  vendor's page, and edits it. Turning Purchase orders off again must take Vendors with it. Then it closes the app and checks the database with Python's
  sqlite3. Runs off-screen and never takes focus. Exits 1 on the first step that does not happen.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir = (Join-Path $env:TEMP ("profitdjinn-po-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$real = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProfitDjinn'
$DataDir = [System.IO.Path]::GetFullPath($DataDir)
if ($DataDir.TrimEnd('\') -ieq $real) { throw "Refusing to run against the real data folder." }
New-Item -ItemType Directory -Force $DataDir | Out-Null
if (Test-Path (Join-Path $DataDir 'app.db')) { throw "$DataDir already has an app.db. Give an empty folder." }

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.Arguments = "--page settings:features"
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["PROFITDJINN_DATA_DIR"] = $DataDir
$psi.EnvironmentVariables["PROFITDJINN_OFFSCREEN"] = "1"
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
function Invoke-Link([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Hyperlink)
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 400
}
function Switch-Of([string] $name) { return Wait-For $name -type ([System.Windows.Automation.ControlType]::CheckBox) }
function Toggle([string] $name) {
  (Switch-Of $name).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Milliseconds 300
}
function Is-On([string] $name) {
  return (Switch-Of $name).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On'
}
function Step([string] $what, [scriptblock] $do) {
  Write-Host -NoNewline "  $what ... "
  & $do
  Write-Host "ok"
}

try {
  Write-Host "Smoke test on $DataDir"
  Step "Purchase orders starts off, and so do Vendors" {
    # Save Changes only appears once something has changed; wait for the switch itself.
    Switch-Of "Purchase orders" | Out-Null
    if (Is-On "Purchase orders") { throw "Purchase orders should start off." }
    if (Is-On "Vendors") { throw "Vendors should start off." }
    if ((Find-All "Purchase Orders" ([System.Windows.Automation.ControlType]::Button)).Count -ne 0) { throw "Purchase Orders is in the sidebar while off." }
  }
  Step "turning Purchase orders on turns Vendors on and holds it" {
    Toggle "Purchase orders"
    if (-not (Is-On "Vendors")) { throw "Vendors did not turn on with Purchase orders." }
    if ((Switch-Of "Vendors").Current.IsEnabled) { throw "Vendors can still be switched off while Purchase orders is on." }
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Wait-For "Purchase Orders" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Wait-For "Vendors" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
  }
  Step "set the letterhead and a PO prefix" {
    Click "Business settings"
    Type-Into "Business name" "Smoke Glass LLC"
    Type-Into "Contact line" "Pat Smoke, Owner"
    Type-Into "Services" "Glass replacement`nShower doors"
    Click "Purchase Orders settings"
    Type-Into "Number prefix" "MG"
    Type-Into "Starting number" "500"
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
  }
  Step "new PO for a vendor added from the PO form, with its address" {
    Click "Purchase Orders"
    Wait-For "No purchase orders found." | Out-Null
    Click "New Purchase Order"
    Wait-For "Save Purchase Order" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    $number = (Wait-For "PO #" -type ([System.Windows.Automation.ControlType]::Edit)).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($number -ne "MG0500") { throw "The new PO number is '$number', expected MG0500." }
    Type-Into "Vendor" "Smoke Glass Supply"
    Click "Add Vendor"
    Wait-For "Vendor Name" -type ([System.Windows.Automation.ControlType]::Edit) | Out-Null
    Type-Into "Address" "100 Industrial Rd"
    Type-Into "City" "Omaha"
    Type-Into "State" "NE"
    Type-Into "ZIP" "68102"
    Click "Add Vendor"          # the dialog's button (the last one found)
    Wait-For "Vendor 'Smoke Glass Supply' created." | Out-Null
    Type-Into "Description" "Clear tempered 36 x 60"
    Type-Into "Qty" "2"
    Type-Into "0.00" "45.50"
    Click "Add Line"
    Type-Into "Description" "Glazing tape"
    Type-Into "0.00" "12.25"
    Type-Into "Notes" "Deliver before 10am."
    Click "Save Purchase Order"
    Wait-For "Purchase order MG0500 created." | Out-Null
    Wait-For "`$103.25" | Out-Null
  }
  Step "preview the PO without saving it" {
    Click "Preview"
    Wait-For "Save PDF" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Click "Close"
  }
  Step "mark it received" {
    Click "Mark Received"
    Wait-For "Purchase order MG0500 marked received." | Out-Null
    Wait-For "Reopen" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
  }
  Step "the vendor's page lists the PO" {
    Invoke-Link "Smoke Glass Supply"
    Wait-For "New Purchase Order" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Invoke-Link "MG0500"
    Wait-For "Reopen" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
  }
  Step "edit the PO" {
    Click "Edit"
    Wait-For "Save Purchase Order" -type ([System.Windows.Automation.ControlType]::Button) | Out-Null
    Start-Sleep -Milliseconds 500
    Click "Add Line"
    Type-Into "Description" "Screen kit"
    Type-Into "0.00" "5"
    Click "Save Purchase Order"
    Wait-For "Purchase order MG0500 updated." | Out-Null
    Wait-For "`$108.25" | Out-Null
  }
  Step "turning Purchase orders off hides Vendors again" {
    Click "Settings"
    Click "Features settings"
    if (-not (Is-On "Vendors")) { throw "Vendors should show On while Purchase orders is on." }
    Toggle "Purchase orders"
    if (Is-On "Vendors") { throw "Vendors stayed on although it was only on for Purchase orders." }
    if (-not (Switch-Of "Vendors").Current.IsEnabled) { throw "Vendors is still locked." }
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Start-Sleep -Milliseconds 500
    foreach ($nav in "Vendors", "Purchase Orders") {
      if ((Find-All $nav ([System.Windows.Automation.ControlType]::Button)).Count -ne 0) { throw "$nav is still in the sidebar." }
    }
  }
}
catch {
  Write-Host ""
  Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
  if (-not $proc.HasExited) { $proc.Kill() }
  exit 1
}
finally {
  if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $proc.HasExited) { $proc.Kill() } }
}

Write-Host -NoNewline "  check the database ... "
$check = @"
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
po = c.execute("select po_number, status, received_date is not null, notes from purchase_orders").fetchall()
lines = c.execute("select description, quantity, unit_price, amount from purchase_order_lines order by position").fetchall()
vendor = c.execute("select name, address, city, state, zip_code from vendors").fetchall()
s = dict(c.execute("select key, value from settings where key in ('vendors_enabled','purchase_orders_enabled','company_services')").fetchall())
problems = []
if po != [("MG0500", "received", 1, "Deliver before 10am.")]: problems.append(f"purchase orders: {po}")
if lines != [("Clear tempered 36 x 60", 2.0, 45.5, 91.0), ("Glazing tape", 1.0, 12.25, 12.25), ("Screen kit", 1.0, 5.0, 5.0)]: problems.append(f"lines: {lines}")
if vendor != [("Smoke Glass Supply", "100 Industrial Rd", "Omaha", "NE", "68102")]: problems.append(f"vendor: {vendor}")
if s.get("vendors_enabled") != "false" or s.get("purchase_orders_enabled") != "false": problems.append(f"switches: {s}")
if s.get("company_services") != "Glass replacement\nShower doors": problems.append(f"services: {s.get('company_services')!r}")
print("; ".join(problems) if problems else "ok")
sys.exit(1 if problems else 0)
"@
$script_ = Join-Path $DataDir 'check.py'
Set-Content -Path $script_ -Value $check -Encoding ascii
& python -I $script_ (Join-Path $DataDir 'app.db')
if ($LASTEXITCODE -ne 0) { exit 1 }
Write-Host "Purchase orders smoke test passed."
