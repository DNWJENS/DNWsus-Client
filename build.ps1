# Baut DNWsus-Client und legt die fertige exe direkt in den Ordner Releases.
# Aufruf:  .\build.ps1 1.0.1
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$proj    = Join-Path $root 'WindowsUpdateManager\WindowsUpdateManager.csproj'
$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
$name    = "dnwsus-client_v$Version"

# Versionsnummer in AssemblyInfo.cs und im Fenstertitel nachziehen
$asm = Join-Path $root 'WindowsUpdateManager\Properties\AssemblyInfo.cs'
(Get-Content $asm -Raw -Encoding UTF8) -replace '"\d+\.\d+\.\d+\.0"', "`"$Version.0`"" |
    Set-Content $asm -Encoding UTF8 -NoNewline
$form = Join-Path $root 'WindowsUpdateManager\Forms\MainForm.cs'
(Get-Content $form -Raw -Encoding UTF8) -replace 'DNWsus-Client v\d+\.\d+\.\d+', "DNWsus-Client v$Version" |
    Set-Content $form -Encoding UTF8 -NoNewline

& $msbuild $proj /p:Configuration=Release /p:AssemblyName=$name /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

$exe = Join-Path $root "WindowsUpdateManager\bin\Release\$name.exe"
Copy-Item $exe (Join-Path $root 'Releases') -Force
Write-Host "Fertig: Releases\$name.exe" -ForegroundColor Green
