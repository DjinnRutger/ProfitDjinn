# Builds ProfitDjinn 2.0: runs the tests, then publishes one portable ProfitDjinn.exe.
#
#   .\build.ps1              tests + publish
#   .\build.ps1 -SkipTests   publish only
#
# Output: <DevRoot>\publish\ProfitDjinn.exe. DevRoot is artifacts\ in the repo unless
# Directory.Build.local.props sets it (C:\Dev\ProfitDjinn\dotnet\ on Jon's machines).
# The exe needs nothing installed on the user's PC: .NET is bundled inside it.
# (The 1.x Flask build is still build.bat until 2.0 ships.)

param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$devRoot = (dotnet msbuild src\ProfitDjinn.Core\ProfitDjinn.Core.csproj -getProperty:DevRoot -nologo).Trim()
if ($LASTEXITCODE -ne 0 -or -not $devRoot) { throw "Could not read DevRoot from Directory.Build.props." }
$out = Join-Path $devRoot 'publish'

if (-not $SkipTests) {
    dotnet test tests\ProfitDjinn.Tests --nologo --results-directory (Join-Path $devRoot 'TestResults')
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Not publishing." }
}

dotnet publish src\ProfitDjinn.App -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $out 'ProfitDjinn.exe'
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Built $exe ($mb MB)"
Write-Host "Copy that one file to the user's PC and run it. No install needed."
