# Installiert bzw. aktualisiert den DNWsus-Server als Windows-Dienst.
# Vorläufig, bis es eine Server-MSI gibt (Etappe 5).
# Aufruf als Administrator im entpackten Release-Ordner:  .\install-server.ps1
#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "$env:ProgramFiles\DNWsus\Server",
    [int]$Port = 13162
)

$ErrorActionPreference = 'Stop'
$serviceName = 'DnwSusServer'
$source      = Join-Path $PSScriptRoot 'Server'

if (-not (Test-Path (Join-Path $source 'DnwSus.Server.exe'))) {
    throw "Ordner 'Server' mit DnwSus.Server.exe nicht gefunden neben diesem Script."
}

$os = Get-CimInstance Win32_OperatingSystem
if ([int]$os.BuildNumber -lt 17763) {
    throw "Nicht unterstützt: $($os.Caption). Benötigt wird Windows Server 2019 oder neuer."
}

# Laufenden Dienst für das Update anhalten
$service = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Write-Host "Stoppe Dienst $serviceName ..."
    Stop-Service $serviceName -Force
    $service.WaitForStatus('Stopped', '00:00:30')
}

# Dateien kopieren; eine angepasste appsettings.json bleibt erhalten
Write-Host "Kopiere Dateien nach $InstallDir ..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$keepSettings = Test-Path (Join-Path $InstallDir 'appsettings.json')
Get-ChildItem $source | Where-Object { -not ($keepSettings -and $_.Name -eq 'appsettings.json') } |
    Copy-Item -Destination $InstallDir -Recurse -Force

$exe = Join-Path $InstallDir 'DnwSus.Server.exe'
if (-not $service) {
    Write-Host "Lege Dienst $serviceName an ..."
    New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName 'DNWsus Server' `
        -Description 'DNWsus: Dashboard und Agent-Schnittstelle für Windows-Updates der Clients.' `
        -StartupType Automatic | Out-Null
    # Bei Absturz nach 1 Minute neu starten
    & sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
}

# Firewall: Port für Agenten und MSI-Download (das Dashboard selbst ist nur lokal erreichbar)
$ruleName = 'DNWsus Server (TCP ' + $Port + ')'
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    Write-Host "Lege Firewall-Regel '$ruleName' an ..."
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port `
        -Action Allow -Profile Domain | Out-Null
}

Write-Host "Starte Dienst ..."
Start-Service $serviceName

$fqdn = ([System.Net.Dns]::GetHostEntry('')).HostName.ToLower()
Write-Host ""
Write-Host "Fertig. Dashboard (nur auf diesem Server): https://$($fqdn):$Port" -ForegroundColor Green
