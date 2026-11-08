# Removes the XeneonTelemetryHost service. Elevated PowerShell only.
# Does not uninstall PawnIO and does not edit the firewall.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Service removal needs an elevated PowerShell."
}
sc.exe stop XeneonTelemetryHost | Out-Null
sc.exe delete XeneonTelemetryHost
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Service deleted. Token files were left on disk."
