#!/bin/sh
# Publish a self-contained win-x64 host and wrap it with NSIS.
# PublishTrimmed is false so LibreHardwareMonitor reflection and resources stay intact.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
PAY=$(mktemp -d)
OUT="$ROOT/host/installer/dist"
VER=$(sed -n 's/^!define PRODUCT_VERSION "\(.*\)"/\1/p' "$ROOT/host/installer/XeneonHost.nsi")
test -n "$VER"
mkdir -p "$OUT"
dotnet publish "$ROOT/host/src/Diego.Telemetry.Host/Diego.Telemetry.Host.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishTrimmed=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$PAY"
dotnet publish "$ROOT/host/src/Diego.Telemetry.Desktop/Diego.Telemetry.Desktop.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishTrimmed=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$PAY"
mkdir -p "$PAY/config"
cp "$ROOT/host/config/example.host.json" "$PAY/config/example.host.json"
cp "$ROOT/host/config/example.shutdown.json" "$PAY/config/shutdown.example.json"
cp "$ROOT/host/installer/FIREWALL.txt" "$PAY/FIREWALL.txt"
cp "$ROOT/host/installer/Repair-PrivateFirewall.ps1" "$PAY/Repair-PrivateFirewall.ps1"
cp "$ROOT/host/installer/shortcuts/"*.cmd "$PAY/"
cp "$ROOT/LICENSE" "$PAY/LICENSE.txt"
(
  cd "$ROOT/host/installer"
  makensis -DPAYLOAD="$PAY" XeneonHost.nsi
)
mv -f "$ROOT/host/installer/XeneonHwMonitorHost-$VER-win-x64.exe" "$OUT/XeneonHwMonitorHost-$VER-win-x64.exe"
# NSIS writes CheckSum as 0. Fill in the normal PE checksum. This does not sign the file.
python3 "$ROOT/host/installer/fix-pe-checksum.py" "$OUT/XeneonHwMonitorHost-$VER-win-x64.exe"
rm -rf "$PAY"
echo "$OUT/XeneonHwMonitorHost-$VER-win-x64.exe"
