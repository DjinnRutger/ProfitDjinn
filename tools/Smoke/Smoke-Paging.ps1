<#
  2.8. Checks that the Invoices and Customers lists stay fast at a real business's size and
  that their pagers work, driven through UI Automation on a throwaway data folder.

    .\tools\Smoke\Smoke-Paging.ps1 -Exe <ProfitDjinn.exe> [-DataDir <empty folder>]

  Starts the app once so it creates an empty database, closes it, then fills it with Python's
  sqlite3: 850 customers and 1,600 one-line invoices (made-up names). Before 2.8 the Invoices
  list took about 28 s to open at that size, because it built a row for every invoice; each list
  now has to open within 15 s and show 50 rows a page. Then it pages forward, jumps to the last
  page, and checks the totals still cover every invoice. Runs off-screen and never takes focus.
  Exits 1 on the first step that does not happen.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir = (Join-Path $env:TEMP ("profitdjinn-paging-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$real = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProfitDjinn'
$DataDir = [System.IO.Path]::GetFullPath($DataDir)
if ($DataDir.TrimEnd('\') -ieq $real) { throw "Refusing to run against the real data folder." }
New-Item -ItemType Directory -Force $DataDir | Out-Null
if (Test-Path (Join-Path $DataDir 'app.db')) { throw "$DataDir already has an app.db. Give an empty folder." }

function Start-App([string] $page) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
  $psi.Arguments = "--page $page"
  $psi.UseShellExecute = $false
  $psi.EnvironmentVariables["PROFITDJINN_DATA_DIR"] = $DataDir
  $psi.EnvironmentVariables["PROFITDJINN_OFFSCREEN"] = "1"
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $p = [System.Diagnostics.Process]::Start($psi)
  for ($i = 0; $i -lt 240 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  if ($p.MainWindowHandle -eq 0) { $p.Kill(); throw "No window after 60 s." }
  if ($p.MainWindowTitle -notlike '*test data*') { $p.Kill(); throw "App ignored PROFITDJINN_DATA_DIR." }
  $script:proc = $p
  $script:opened = $sw.Elapsed.TotalSeconds
  $script:root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}
function Stop-App {
  if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $proc.HasExited) { $proc.Kill() } }
}
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
# The pager text uses an en dash; PowerShell 5.1 reads this file as ANSI, so match it with "?".
function Wait-Like([string] $pattern, [int] $seconds = 10) {
  for ($i = 0; $i -lt $seconds * 4; $i++) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.Name -like $pattern) { return $e } }
    Start-Sleep -Milliseconds 250
  }
  throw "Timed out waiting for text like '$pattern'."
}
# Invoice-number links on the page (the seeded numbers are 5001-6600).
function Count-InvoiceLinks {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Hyperlink)
  return @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | Where-Object { $_.Current.Name -match '^(5\d{3}|6[0-6]\d{2})$' }).Count
}
function Step([string] $what, [scriptblock] $do) {
  Write-Host -NoNewline "  $what ... "
  & $do
  Write-Host "ok"
}

try {
  Step "create an empty database" {
    Start-App "dashboard"
    Stop-App
    if (-not (Test-Path (Join-Path $DataDir 'app.db'))) { throw "No app.db was created." }
  }

  Step "fill it with 850 customers and 1,600 invoices" {
    $py = @'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
now = "2026-01-01 00:00:00.000000"
with db:
    for n in range(1, 851):
        db.execute("INSERT INTO customers (name, city, state, is_active, created_at) VALUES (?, 'Springfield', 'IL', 1, ?)", (f"Paging Customer {n:03d}", now))
    for n in range(1, 1601):
        cur = db.execute("INSERT INTO invoices (invoice_number, customer_id, date, notes, term1, term2, paid, paid_date, credit_applied, created_at)"
                         " VALUES (?, ?, ?, '', '', '', 0, NULL, 0, ?)", (str(5000 + n), (n % 850) + 1, f"2025-{(n % 12) + 1:02d}-15", now))
        db.execute("INSERT INTO invoice_lines (invoice_id, description, quantity, amount) VALUES (?, 'Window repair', 1, 10.0)", (cur.lastrowid,))
'@
    $script_ = Join-Path $DataDir 'seed.py'
    Set-Content -Path $script_ -Value $py -Encoding ascii
    & python -I $script_ (Join-Path $DataDir 'app.db')
    if ($LASTEXITCODE -ne 0) { throw "Seeding failed." }
  }

  Step "Invoices opens within 15 s and shows 50 rows" {
    Start-App "invoices"
    Wait-Like "Showing 1?50 of 1,600 invoices" | Out-Null
    if ($opened -gt 15) { throw "The Invoices list took $([math]::Round($opened, 1)) s to open." }
    $rows = Count-InvoiceLinks
    if ($rows -ne 50) { throw "Expected 50 invoice rows, found $rows." }
    Write-Host -NoNewline "($([math]::Round($opened, 1)) s) "
  }

  Step "the totals cover all 1,600 invoices, not just the page" {
    Wait-For "Total, all 1,600" | Out-Null
    Wait-For "`$16000.00" | Out-Null   # Ui.Money: no thousands separator, as in 1.x
  }

  Step "Next page shows rows 51-100" {
    $before = (Find-All "Previous page" ([System.Windows.Automation.ControlType]::Button))[0]
    if ($before.Current.IsEnabled) { throw "Previous should be disabled on the first page." }
    Click "Next page"
    Wait-Like "Showing 51?100 of 1,600 invoices" | Out-Null
  }

  Step "the last page holds the oldest invoice and Next is disabled" {
    Click "Page 32"
    Wait-Like "Showing 1,551?1,600 of 1,600 invoices" | Out-Null
    $next = (Find-All "Next page" ([System.Windows.Automation.ControlType]::Button))[0]
    if ($next.Current.IsEnabled) { throw "Next should be disabled on the last page." }
  }
  Stop-App

  Step "Customers opens within 15 s and pages" {
    Start-App "customers"
    Wait-Like "Showing 1?50 of 850 customers" | Out-Null
    if ($opened -gt 15) { throw "The Customers list took $([math]::Round($opened, 1)) s to open." }
    Write-Host -NoNewline "($([math]::Round($opened, 1)) s) "
    Click "Next page"
    Wait-Like "Showing 51?100 of 850 customers" | Out-Null
    Wait-For "Paging Customer 051" | Out-Null
  }
  Stop-App
  Write-Host "Smoke-Paging passed."
}
catch {
  Write-Host ""
  Write-Host "FAILED: $_" -ForegroundColor Red
  if ($proc -and -not $proc.HasExited) { $proc.Kill() }
  exit 1
}
