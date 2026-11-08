# Arms Pi poweroff on the already installed manual service.
# Elevated, explicit, and refused while the example URL or a missing token remains.
# Stopping the service later still uses sc.exe stop, which is not preshutdown.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Arming Pi shutdown needs an elevated PowerShell. Nothing was changed."
}
$data = Join-Path $env:ProgramData "Xeneon\HW Monitor Host"
$shutdown = Join-Path $data "config\shutdown.json"
$marker = Join-Path $data "service.opted-in"
if (-not (Test-Path $marker)) {
    Write-Error "The Windows service was not opted in. Re-run setup and select the service section first."
}
if (-not (Test-Path $shutdown)) {
    Write-Error "Copy config\shutdown.example.json to config\shutdown.json and edit piBaseUrl and shutdownTokenFile. The example URL is not armed."
}
$raw = Get-Content -Raw -Path $shutdown
if ($raw -match "192\.168\.1\.40") {
    Write-Error "shutdown.json still contains the example address 192.168.1.40. Edit it. Poweroff was not armed."
}
if ($raw -notmatch '"piBaseUrl"\s*:\s*"http') {
    Write-Error "shutdown.json has no piBaseUrl. Poweroff was not armed."
}
$install = (Get-ItemProperty "HKLM:\Software\Xeneon\HW Monitor Host").InstallDir
$exe = Join-Path $install "diego-telemetry-host.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Host executable was not found at $exe"
}
$bin = "`"$exe`" service --shutdown-config `"$shutdown`""
sc.exe stop XeneonTelemetryHost | Out-Null
sc.exe config XeneonTelemetryHost binPath= $bin start= demand
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Set-Content -Path (Join-Path $data "pi-shutdown.opted-in") -Value "opt-in`r`n" -Encoding ascii
Write-Host "Pi shutdown is armed for the next PC shutdown handled by this service."
Write-Host "The service was not started. sc.exe stop does not send preshutdown."
Write-Host "Windows 10 build 15063 and later wait only 10 seconds unless SERVICE_CONFIG_PRESHUTDOWN_INFO is set. Default grace wants 50000 ms. Set that with ChangeServiceConfig2 before relying on the full wait. This script does not change WaitToKillServiceTimeout and does not change the firewall."
