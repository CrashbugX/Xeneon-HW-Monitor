#!/bin/sh
# Stop this user's Xeneon bridge, then start the user service again.
# A manual demo is stopped only when its command line contains xeneon.
# Browsers and other Python programs are left running.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run sh client/scripts/restart-bridge.sh as the desktop user, not root." >&2
  exit 1
fi
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
STOP_ONLY=0
if [ "${1:-}" = "--stop-only" ]; then
  STOP_ONLY=1
fi

if command -v systemctl >/dev/null 2>&1; then
  systemctl --user stop xeneon-bridge.service >/dev/null 2>&1 || true
fi

for cmdline in /proc/[0-9]*/cmdline; do
  [ -r "$cmdline" ] || continue
  text=$(tr '\0' ' ' < "$cmdline" || true)
  case "$text" in
    *python*-m\ xeneon*|*/xeneon/__main__.py*)
      pid=${cmdline#/proc/}
      pid=${pid%/cmdline}
      if [ "$pid" = "$$" ]; then
        continue
      fi
      echo "Stopping Xeneon bridge pid $pid"
      kill -TERM "$pid" 2>/dev/null || true
      ;;
  esac
done

if [ "$STOP_ONLY" -eq 1 ]; then
  echo "Stopped the Xeneon bridge only."
  exit 0
fi

if command -v systemctl >/dev/null 2>&1; then
  systemctl --user daemon-reload || true
  systemctl --user restart xeneon-bridge.service || echo "The user service did not start. sh $ROOT/scripts/kiosk.sh can start the bridge when you are at the desktop."
fi
echo "Bridge restart requested. Config remains in $CONFIG_DIR"
