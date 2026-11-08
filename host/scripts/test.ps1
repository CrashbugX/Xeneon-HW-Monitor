$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Write-Host "Host directory: $root"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "The .NET SDK is not on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and open a new terminal."
}

Write-Host "dotnet $(dotnet --version)"
dotnet build "$root\Diego.Telemetry.slnx" -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test "$root\Diego.Telemetry.slnx" -c Release --no-build
exit $LASTEXITCODE
