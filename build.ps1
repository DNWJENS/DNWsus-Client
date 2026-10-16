# Baut DNWsus komplett und legt die Server-MSI unter Releases\<Version> ab.
# Aufruf:  .\build.ps1 2.1.0
#
# Ablauf: Agent + Hinweis bauen -> Agent-MSI (neutrale Vorlage) -> Server veröffentlichen (self-contained),
#         Vorlage nach Server\agent\ legen -> Server-MSI. Die Agent-MSI für die Clients erzeugt später der
#         installierte Server (mit seiner Adresse und seinem Zertifikat).
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$root   = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

function Invoke-Dotnet([string]$step, [string[]]$arguments) {
    Write-Host "== $step" -ForegroundColor Cyan
    & $dotnet @arguments --nologo -v minimal "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "$step fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
}

$out     = Join-Path $root "Releases\$Version"
$staging = Join-Path $root 'Releases\_staging'
foreach ($dir in $out, $staging) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }

Invoke-Dotnet 'Agent'      @('build', (Join-Path $root 'src\DnwSus.Agent\DnwSus.Agent.csproj'), '-c', 'Release')
Invoke-Dotnet 'Hinweis'    @('build', (Join-Path $root 'src\DnwSus.Hinweis\DnwSus.Hinweis.csproj'), '-c', 'Release')
Invoke-Dotnet 'Agent-MSI'  @('build', (Join-Path $root 'setup\Agent\DnwSus.Agent.Setup.wixproj'), '-c', 'Release')

$serverDir = Join-Path $staging 'Server'
Invoke-Dotnet 'Server'     @('publish', (Join-Path $root 'src\DnwSus.Server\DnwSus.Server.csproj'), '-c', 'Release', '-o', $serverDir)

New-Item -ItemType Directory -Force (Join-Path $serverDir 'agent') | Out-Null
Copy-Item (Join-Path $root 'setup\Agent\bin\x64\Release\DnwSus-Agent.msi') (Join-Path $serverDir 'agent\DnwSus-Agent.msi')

Invoke-Dotnet 'Server-MSI' @('build', (Join-Path $root 'setup\Server\DnwSus.Server.Setup.wixproj'), '-c', 'Release', "-p:PublishDir=$serverDir")

New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root 'setup\Server\bin\x64\Release\DnwSus-Server.msi') (Join-Path $out "DnwSus-Server-$Version.msi")
Copy-Item (Join-Path $root 'docs\Installation.md') $out
Remove-Item $staging -Recurse -Force

Write-Host "Fertig: Releases\$Version\DnwSus-Server-$Version.msi" -ForegroundColor Green
