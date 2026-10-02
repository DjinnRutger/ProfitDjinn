<#
  Screenshots of a page in all three themes, on a fresh copy of the parity fixture.

    .\tools\Smoke\Shoot-Themes.ps1 -Exe <ProfitDjinn.exe> -OutDir <folder> [-Name dash] [-Page gallery]

  Each theme gets its own copy of tests\ProfitDjinn.Tests\Fixtures\parity.db with the theme
  setting written in, so nothing is shared and the real database is never involved.
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [Parameter(Mandatory)] [string] $OutDir,
  [string] $Name = "page",
  [string] $Keys = "",
  [string] $Page = "",
  [string[]] $Themes = @("light", "dark", "terminal"),
  [int] $Wait = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path $root "tests\ProfitDjinn.Tests\Fixtures\parity.db"
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($theme in $Themes) {
  $data = Join-Path $OutDir "data-$theme"
  Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force $data | Out-Null
  Copy-Item $fixture (Join-Path $data "app.db")
  # Write the theme the same way the app stores it.
  $py = @"
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
if c.execute("select count(*) from settings where key='theme'").fetchone()[0]:
    c.execute("update settings set value=? where key='theme'", (sys.argv[2],))
else:
    c.execute("insert into settings (key, value, type, category) values ('theme', ?, 'text', 'appearance')", (sys.argv[2],))
c.commit()
"@
  $py | python - (Join-Path $data "app.db") $theme
  & (Join-Path $PSScriptRoot "Capture.ps1") -Exe $Exe -DataDir $data -Out (Join-Path $OutDir "$Name-$theme.png") -Keys $Keys -Page $Page -Wait $Wait
}
