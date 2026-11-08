# Recreate the Private-network rules for the ports in host.json.
# Run this from an elevated PowerShell yourself. This script does not relaunch
# itself as administrator, so Windows shows the usual consent prompt only when you elevate.
# It does not change the Public profile and it does not turn the firewall off.
$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This needs an elevated PowerShell. Windows will ask if you start one yourself."
    Write-Host "Right-click PowerShell, choose Run as administrator, then run:"
    Write-Host ("  powershell -NoProfile -ExecutionPolicy Bypass -File `"" + $PSCommandPath + "`"")
    exit 2
}

$data = Join-Path $env:ProgramData "Xeneon\HW Monitor Host\config\host.json"
$publish = 8765
$discovery = 8764
if (Test-Path -LiteralPath $data) {
    $config = Get-Content -LiteralPath $data -Raw | ConvertFrom-Json
    if ($config.publishPort) { $publish = [int]$config.publishPort }
    if ($config.discoveryPort) { $discovery = [int]$config.discoveryPort }
}
if ($publish -lt 1024 -or $publish -gt 65535 -or $discovery -lt 1024 -or $discovery -gt 65535) {
    Write-Host "Refusing ports outside 1024-65535."
    exit 2
}

$names = @(
    "Xeneon HW Monitor Host",
    "Xeneon HW Monitor Host TCP",
    "Xeneon HW Monitor Host UDP"
)
foreach ($name in $names) {
    netsh advfirewall firewall delete rule name="$name" | Out-Host
}
netsh advfirewall firewall add rule name="Xeneon HW Monitor Host TCP" dir=in action=allow protocol=TCP localport=$publish profile=private remoteip=localsubnet | Out-Host
netsh advfirewall firewall add rule name="Xeneon HW Monitor Host UDP" dir=in action=allow protocol=UDP localport=$discovery profile=private remoteip=localsubnet | Out-Host
Write-Host "Private profile only. TCP $publish and UDP $discovery, local subnet. Public was not changed."
