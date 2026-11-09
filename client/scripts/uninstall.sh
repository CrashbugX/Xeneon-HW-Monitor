#!/bin/sh
# Remove the user service and autostart entry. Leaves token files in place
# unless --purge-config is passed. Does not edit boot configuration.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run client/scripts/uninstall.sh as the desktop user, not root." >&2
  exit 1
fi
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
UNIT_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user
AUTOSTART=${XDG_CONFIG_HOME:-$HOME/.config}/autostart
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
APP_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/applications
LABWC=${XDG_CONFIG_HOME:-$HOME/.config}/labwc
sh "$ROOT/scripts/kiosk.sh" handoff || true
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user disable --now xeneon-bridge.service || true
  systemctl --user daemon-reload || true
fi
rm -f "$UNIT_DIR/xeneon-bridge.service" "$AUTOSTART/xeneon-kiosk.desktop" "$APP_DIR/xeneon-dashboard.desktop"
if [ -f "$LABWC/autostart" ]; then
  tmp_auto=$(mktemp)
  grep -v 'scripts/kiosk.sh' "$LABWC/autostart" > "$tmp_auto" || true
  mv "$tmp_auto" "$LABWC/autostart"
fi
if [ "${1:-}" = "--purge-config" ]; then
  rm -rf "$CONFIG_DIR"
  echo "Removed $CONFIG_DIR"
fi
echo "User autostart removed."
