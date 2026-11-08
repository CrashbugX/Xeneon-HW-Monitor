#!/bin/sh
# Reverse overlay-enable.sh using the backup it wrote. Refuses to guess a cmdline.
set -eu
BOOT=""
for candidate in /boot/firmware/cmdline.txt /boot/cmdline.txt; do
  if [ -f "$candidate.xeneon-backup" ]; then
    BOOT=$candidate
    break
  fi
done
if [ -z "$BOOT" ]; then
  echo "No xeneon cmdline backup was found. Disable the overlay in raspi-config instead of editing by hand." >&2
  exit 2
fi
cp -a "$BOOT.xeneon-backup" "$BOOT"
echo "Restored $BOOT from the xeneon backup. Reboot to leave the overlay. This does not guarantee an in-progress power cut is safe."
