#!/bin/sh
# Publish a self-contained win-x64 host and wrap it with NSIS.
# PublishTrimmed is false so LibreHardwareMonitor reflection and resources stay intact.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
PAY=$(mktemp -d)
OUT="$ROOT/host/installer/dist"
mkdir -p "$OUT"
dotnet publish "$ROOT/host/src/Diego.Telemetry.Host/Diego.Telemetry.Host.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishTrimmed=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$PAY"
mkdir -p "$PAY/config"
cp "$ROOT/host/config/example.host.json" "$PAY/config/example.host.json"
cp "$ROOT/host/config/example.shutdown.json" "$PAY/config/shutdown.example.json"
cp "$ROOT/host/installer/FIREWALL.txt" "$PAY/FIREWALL.txt"
cp "$ROOT/host/installer/shortcuts/"*.cmd "$PAY/"
cp "$ROOT/LICENSE" "$PAY/LICENSE.txt"
(
  cd "$ROOT/host/installer"
  makensis -DPAYLOAD="$PAY" XeneonHost.nsi
)
mv -f "$ROOT/host/installer/XeneonHwMonitorHost-0.1.0-win-x64.exe" "$OUT/XeneonHwMonitorHost-0.1.0-win-x64.exe"
# NSIS writes CheckSum as 0. Fill in the normal PE checksum. This does not sign the file.
python3 "$ROOT/host/installer/fix-pe-checksum.py" "$OUT/XeneonHwMonitorHost-0.1.0-win-x64.exe"
rm -rf "$PAY"
echo "$OUT/XeneonHwMonitorHost-0.1.0-win-x64.exe"
