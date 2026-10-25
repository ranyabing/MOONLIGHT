param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build'),
    [string]$TestDirectory = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PATHEXT = '.COM;.EXE;.BAT;.CMD'
$repository = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repository 'src'
if (-not $TestDirectory) { $TestDirectory = Join-Path $OutputDirectory 'tests' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $TestDirectory -Force | Out-Null
$application = Join-Path $OutputDirectory 'MOONLIGHT.exe'
$common = @('/nologo', '/optimize+', '/utf8output', '/codepage:65001', '/warn:4', '/warnaserror+',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
$applicationArgs = $common + @('/target:winexe', '/platform:anycpu', "/out:$application",
    "/win32icon:$(Join-Path $source 'MOONLIGHT.ico')", "/win32manifest:$(Join-Path $source 'app.manifest')")
$applicationArgs += @(Get-ChildItem -LiteralPath $source -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
& $compiler @applicationArgs
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$testExecutable = Join-Path $TestDirectory 'Moonlight.Tests.exe'
$testArgs = $common + @('/target:exe', '/platform:anycpu', "/out:$testExecutable", "/r:$application",
    "/win32manifest:$(Join-Path $source 'app.manifest')", (Join-Path $repository 'tests\Moonlight.Tests.cs'))
& $compiler @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath $application -Destination (Join-Path $TestDirectory 'MOONLIGHT.exe') -Force
[pscustomobject]@{ Application=$application; Tests=$testExecutable; Bytes=(Get-Item -LiteralPath $application).Length } | ConvertTo-Json
