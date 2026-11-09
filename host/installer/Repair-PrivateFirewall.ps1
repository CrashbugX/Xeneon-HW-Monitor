# Recreate the Private and Domain rules for the ports in host.json.
# The rules point at XeneonHwMonitor.exe. Public is not opened.
# The firewall is not turned off. Re-running deletes the same names and adds them again.
param(
    [string]$Program = "",
    [switch]$RemoveOnly
)
$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This needs an elevated PowerShell. The host window can do it with Fix firewall."
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
if (-not $Program) {
    $Program = Join-Path $PSScriptRoot "XeneonHwMonitor.exe"
}

$names = @(
    "Xeneon HW Monitor Host",
    "Xeneon HW Monitor Host TCP",
    "Xeneon HW Monitor Host UDP",
    "Xeneon HW Monitor - TCP $publish",
    "Xeneon HW Monitor - UDP $discovery"
)
foreach ($name in $names) {
    netsh advfirewall firewall delete rule name="$name" | Out-Host
}
if ($RemoveOnly) {
    Write-Host "Removed this app's firewall rules."
    exit 0
}
netsh advfirewall firewall add rule name="Xeneon HW Monitor - TCP $publish" dir=in action=allow protocol=TCP localport=$publish profile=private,domain remoteip=localsubnet program="$Program" | Out-Host
netsh advfirewall firewall add rule name="Xeneon HW Monitor - UDP $discovery" dir=in action=allow protocol=UDP localport=$discovery profile=private,domain remoteip=localsubnet program="$Program" | Out-Host
Write-Host "Private and Domain. TCP $publish and UDP $discovery, local subnet, program $Program. Public was not changed."
