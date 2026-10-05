<#
  End-to-end smoke test of 2.6 Bank Accounts in a built ProfitDjinn.exe, driven through UI
  Automation, on an empty data folder (PROFITDJINN_DATA_DIR, never the real database).

    .	ools\Smoke\Smoke-Banking.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Turns Bank Accounts on, adds a checking account ($1000 opening) and a Stripe processor
  account, puts in a $500 owner contribution, invoices a new customer $515 and records the
  payment deposited to Stripe, adds a $15.24 processing fee, pays the rest out to checking
  (pending), marks it cleared, and reconciles checking to $1999.76. Then it checks the
  database: the contribution and the payout are not income (income is only the $515
  payment), the payout is linked both ways, and the reconciled rows are locked.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir = (Join-Path $env:TEMP ("profitdjinn-smoke-bank-" + [guid]::NewGuid().ToString("N")))
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
function Wait-Like([string] $pattern, [int] $seconds = 10) {
  for ($i = 0; $i -lt $seconds * 4; $i++) {
    foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($e.Current.Name -like $pattern) { return $e }
    }
    Start-Sleep -Milliseconds 250
  }
  throw "Timed out waiting for text like '$pattern'."
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
function Invoke-Link([string] $name) {
  $e = Wait-For $name -type ([System.Windows.Automation.ControlType]::Hyperlink)
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 400
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
public static class SmokeWinBank {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function Save-Screenshot([string] $path) {
  $r = New-Object SmokeWinBank+RECT
  [SmokeWinBank]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); [SmokeWinBank]::PrintWindow($proc.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

try {
  Write-Host "Smoke test on $DataDir"
  Step "turn Bank Accounts on" {
    Wait-For "Bank accounts" -type $CT::CheckBox | Out-Null
    Start-Sleep -Milliseconds 500
    if ((Find-All "Banking" $CT::Button).Count -ne 0) { throw "Banking is in the sidebar before it is turned on." }
    Toggle "Bank accounts"
    Click "Save Changes"
    Wait-For "Settings saved." | Out-Null
    Wait-For "Banking" -type $CT::Button | Out-Null
  }
  Step "add a checking account with a `$1000 opening balance" {
    Click "Banking"
    Click "New Account"
    Type-Into "Account Name" "Smoke Checking"
    Type-Into "Opening Balance" "1000"
    Click "Add Account"
    Wait-For "Account 'Smoke Checking' added." | Out-Null
  }
  Step "put in a `$500 owner contribution" {
    Click "Money In"
    Pick "What is it?" "Owner contribution (your own money put in)"
    Type-Into "Amount" "500"
    Click "Add Money In"
    Wait-For "Owner contribution of `$500.00 recorded in 'Smoke Checking'." | Out-Null
  }
  Step "add a Stripe account" {
    Click "Banking"
    Click "New Account"
    Type-Into "Account Name" "Smoke Stripe"
    Pick "Kind" "Payment processor (e.g. Stripe, Square, PayPal)"
    Click "Add Account"
    Wait-For "Account 'Smoke Stripe' added." | Out-Null
  }
  Step "invoice a customer `$515 and record the payment deposited to Stripe" {
    Click "Invoices"
    Click "New Invoice"
    Wait-For "Save Invoice" -type $CT::Button | Out-Null
    Start-Sleep -Milliseconds 500
    Type-Into "Customer" "Bank Client"
    Click "Add Customer"
    Wait-For "Company / Name" -type $CT::Edit | Out-Null
    Click "Add Customer"
    Wait-For "Customer 'Bank Client' created." | Out-Null
    Click "Add Line"
    Type-Into "Description" "Monthly plan"
    Type-Into "0.00" "515"
    Click "Save Invoice"
    Wait-For "Invoice INV1001 created." | Out-Null
    Click "Record Payment"
    Wait-For "Payment Method" | Out-Null
    Pick "Deposited To" "Smoke Stripe"
    Click "Record Payment"        # the dialog's button (the last one found)
    Wait-For "Payment of `$515.00 recorded. Invoice paid in full." | Out-Null
  }
  Step "record the processing fee in Stripe" {
    Click "Banking"
    Invoke-Link "Smoke Stripe"
    Click "Money Out"
    Pick "What is it?" "Bank or processor fee"
    Type-Into "Amount" "15.24"
    Type-Into "Reference" "ch_smoke"
    Click "Add Money Out"
    Wait-For "Fee of `$15.24 recorded in 'Smoke Stripe'." | Out-Null
  }
  Step "pay the rest out to checking (on its way)" {
    Click "Payout"
    Wait-For "Record Transfer" -type $CT::Button | Out-Null
    Type-Into "Reference" "po_smoke"
    Click "Record Transfer"
    Wait-For "`$499.76 moved from 'Smoke Stripe' to 'Smoke Checking'." | Out-Null
  }
  Step "mark the payout cleared when it arrives" {
    Click "Banking"
    Invoke-Link "Smoke Checking"
    Click "Pending: Payout from Smoke Stripe"
    Wait-For "Marked cleared." | Out-Null
  }
  Step "reconcile checking to the statement" {
    Click "Reconcile"
    Wait-For "Finish Reconciling" -type $CT::Button | Out-Null
    Type-Into "Statement Ending Balance" "1999.76"
    Start-Sleep -Milliseconds 400
    Click "Finish Reconciling"
    Wait-Like "'Smoke Checking' reconciled through*" | Out-Null
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
acc = c.execute("select name, kind, opening_balance, reconciled_balance from bank_accounts order by id").fetchall()
if acc != [("Smoke Checking", "checking", 1000.0, 1999.76), ("Smoke Stripe", "processor", 0.0, None)]: problems.append(f"accounts {acc}")
rows = c.execute("select a.name, t.kind, t.amount, t.status from bank_transactions t join bank_accounts a on a.id = t.account_id order by t.id").fetchall()
want = [("Smoke Checking", "owner_contribution", 500.0, "reconciled"),
        ("Smoke Stripe", "customer_payment", 515.0, "cleared"),
        ("Smoke Stripe", "fee", -15.24, "cleared"),
        ("Smoke Stripe", "transfer", -499.76, "pending"),
        ("Smoke Checking", "transfer", 499.76, "reconciled")]
if rows != want: problems.append(f"transactions {rows}")
pairs = c.execute("select count(distinct transfer_id) from bank_transactions where kind = 'transfer'").fetchone()[0]
if pairs != 1: problems.append(f"transfer pairs {pairs}")
link = c.execute("select count(*) from payment_accounts pa join payments p on p.id = pa.payment_id where p.amount = 515").fetchone()[0]
if link != 1: problems.append(f"payment link {link}")
income = c.execute("select sum(amount) from payments").fetchone()[0]
if income != 515.0: problems.append(f"income {income}")
print("; ".join(problems) if problems else "OK")
"@
$result = $check | python - (Join-Path $DataDir 'app.db')
if ($result -ne "OK") { Write-Host "FAILED: $result"; exit 1 }
Write-Host "ok"
Write-Host "Banking smoke test passed."
