#!/bin/sh
# Re-read this tree's unit files and restart the user service.
# Does not download anything and does not change boot configuration.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run client/scripts/update.sh as the desktop user, not root." >&2
  exit 1
fi
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
"$ROOT/scripts/install.sh"
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user restart xeneon-bridge.service || true
fi
echo "Bridge restart requested from $ROOT"
