# Registers the Windows service that receives SERVICE_CONTROL_PRESHUTDOWN.
# Run from an elevated PowerShell. It does not change the firewall and it does
# not edit WaitToKillServiceTimeout. It is not run by the build.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Service setup needs an elevated PowerShell. Stopping here does not change the firewall."
}
if (-not $args -or $args.Count -lt 1) {
    Write-Error "Pass the full path to diego-telemetry-host.exe and, after it, the arguments the service should use."
}
$exe = $args[0]
$rest = @()
if ($args.Count -gt 1) {
    $rest = $args[1..($args.Count - 1)]
}
$bin = "`"$exe`" service " + ($rest -join " ")
$existing = Get-Service -Name "XeneonTelemetryHost" -ErrorAction SilentlyContinue
if ($existing) {
    sc.exe stop XeneonTelemetryHost | Out-Null
    sc.exe delete XeneonTelemetryHost | Out-Null
    Start-Sleep -Seconds 1
}
sc.exe create XeneonTelemetryHost binPath= $bin start= auto DisplayName= "Xeneon telemetry host"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# dwPreshutdownTimeout milliseconds. Default grace 45s plus 5s margin = 50000.
# Windows 10 build 15063 and later otherwise wait only 10000 ms.
sc.exe failure XeneonTelemetryHost reset= 86400 actions= restart/5000 | Out-Null
Write-Host "Created XeneonTelemetryHost."
Write-Host "Set the preshutdown timeout with:"
Write-Host "  sc.exe qfailure is not the timeout. Use the host's ShutdownPolicy.PreshutdownTimeoutMilliseconds (default 50000)."
Write-Host "ChangeServiceConfig2 SERVICE_CONFIG_PRESHUTDOWN_INFO is documented in docs/CODE-TOUR.md."
Write-Host "Start the service only after the token files exist. This script did not start it."
