# Entfernt den DNWsus-Server-Dienst, die Firewall-Regel und die Programmdateien.
# Das Zertifikat bleibt im Speicher des Computers, damit ausgerollte Agenten bei einer
# Neuinstallation weiter vertrauen. Entfernen mit -RemoveCertificate.
#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "$env:ProgramFiles\DNWsus\Server",
    [switch]$RemoveCertificate
)

$ErrorActionPreference = 'Stop'
$serviceName = 'DnwSusServer'

$service = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Dienst $serviceName entfernt."
}

Get-NetFirewallRule -DisplayName 'DNWsus Server (TCP *' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force; Write-Host "$InstallDir entfernt." }

if ($RemoveCertificate) {
    foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root') {
        Get-ChildItem $store | Where-Object { $_.FriendlyName -eq 'DNWsus Server' -or
            ($store -like '*Root' -and $_.Subject -eq "CN=$(([System.Net.Dns]::GetHostEntry('')).HostName.ToLower())") } |
            Remove-Item
    }
    Write-Host "Zertifikat entfernt. Bereits ausgerollte Agenten brauchen nach einer Neuinstallation eine neue MSI."
}
