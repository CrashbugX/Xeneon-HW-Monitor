#!/bin/sh
# Optional read-only root helper. --check never edits boot files.
# --apply is refused unless this is Raspberry Pi OS and overlayroot or raspi-config exists.
# Enabling an overlay does not make a SATA power cut safe.
set -eu
MODE=${1:---check}
if [ "$MODE" != "--check" ] && [ "$MODE" != "--apply" ]; then
  echo "Use --check or --apply" >&2
  exit 2
fi
if [ ! -r /etc/os-release ]; then
  echo "Cannot read /etc/os-release. Refusing to edit boot configuration." >&2
  exit 2
fi
. /etc/os-release
MODEL=""
if [ -r /proc/device-tree/model ]; then
  MODEL=$(tr -d '\0' < /proc/device-tree/model || true)
fi
echo "Detected ID=${ID:-unknown} VERSION_ID=${VERSION_ID:-unknown} model=${MODEL:-none}"
if ! printf '%s' "$MODEL" | grep -q "Raspberry Pi"; then
  echo "This machine is not reporting a Raspberry Pi device tree. No boot file was changed." >&2
  exit 2
fi
if ! command -v raspi-config >/dev/null 2>&1 && ! dpkg -s overlayroot >/dev/null 2>&1; then
  echo "Neither raspi-config nor the overlayroot package is installed. Refusing a handwritten cmdline edit." >&2
  exit 2
fi
if [ "$MODE" = "--check" ]; then
  echo "Prerequisites are present. Re-run with --apply only when you want the overlay. It is reversible with overlay-disable.sh."
  exit 0
fi
BOOT=""
for candidate in /boot/firmware/cmdline.txt /boot/cmdline.txt; do
  if [ -f "$candidate" ]; then
    BOOT=$candidate
    break
  fi
done
if [ -z "$BOOT" ]; then
  echo "cmdline.txt was not found. Nothing was changed." >&2
  exit 2
fi
cp -a "$BOOT" "$BOOT.xeneon-backup"
if [ ! -f /etc/overlayroot.local.conf ]; then
  printf '%s\n' 'overlayroot="tmpfs:recurse=0"' > /etc/overlayroot.local.conf
fi
echo "Backup written to $BOOT.xeneon-backup."
echo "Finish with: sudo raspi-config  (Performance Options, Overlay File System), then reboot."
echo "recurse=0 is recommended so a separate data partition can stay writable. This script did not append overlayroot to cmdline itself."
exit 0
