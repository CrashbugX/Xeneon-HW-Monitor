$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Write-Host "Host directory: $root"
$out = Join-Path $root "artifacts\win-x64"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "The .NET SDK is not on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and open a new terminal."
}

Write-Host "dotnet $(dotnet --version)"
if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

dotnet publish "$root\src\Diego.Telemetry.Host\Diego.Telemetry.Host.csproj" -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Join-Path $out "diego-telemetry-host.exe"
$lhm = Join-Path $out "LibreHardwareMonitorLib.dll"
if (-not (Test-Path $exe)) {
    Write-Error "Publish finished without diego-telemetry-host.exe."
}
if (-not (Test-Path $lhm)) {
    Write-Error "Publish finished without LibreHardwareMonitorLib.dll. The Windows build is not usable."
}

Write-Host "Published to $out"
Write-Host "Run on the Windows PC: .\\diego-telemetry-host.exe check-backend"
Write-Host "This script does not install PawnIO and does not start the host."
