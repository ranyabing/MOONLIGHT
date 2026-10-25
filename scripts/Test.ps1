param([switch]$Integration, [string]$Application = '')
$ErrorActionPreference = 'Stop'
$env:PATHEXT = '.COM;.EXE;.BAT;.CMD'
$repository = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repository 'build'
if (-not $Application) { $Application = Join-Path $build 'MOONLIGHT.exe' }
$runner = Join-Path $build 'tests\Moonlight.Tests.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Run scripts\Build.ps1 first.' }
$reportDirectory = Join-Path $build 'test-results'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$report = Join-Path $reportDirectory $(if ($Integration) { 'integration.json' } else { 'unit.json' })
$arguments = @($Application, $report)
if (-not $Integration) { $arguments += '--unit-only' }
& $runner @arguments
if ($LASTEXITCODE -ne 0) { throw "Tests failed. See $report" }
