# Baut DNWsus und legt das Ergebnis unter Releases\<Version> ab.
# Aufruf:  .\build.ps1 2.0.0
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$root   = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

$out = Join-Path $root "Releases\$Version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# Server: self-contained, braucht auf dem Zielserver keine .NET-Installation
& $dotnet publish (Join-Path $root 'src\DnwSus.Server\DnwSus.Server.csproj') `
    -c Release -p:Version=$Version -o (Join-Path $out 'Server') --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build Server fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

Copy-Item (Join-Path $root 'setup\install-server.ps1')   $out
Copy-Item (Join-Path $root 'setup\uninstall-server.ps1') $out

Write-Host "Fertig: Releases\$Version" -ForegroundColor Green
