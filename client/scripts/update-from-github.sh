#!/bin/sh
# Download the public source archive and reinstall without touching config.
# Does not copy files from the Windows PC and does not run as root.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run client/scripts/update-from-github.sh as the desktop user, not root." >&2
  exit 1
fi
URL=${XENEON_SOURCE_URL:-https://github.com/CrashbugX/Xeneon-HW-Monitor/archive/refs/heads/main.tar.gz}
DEST=${XDG_CACHE_HOME:-$HOME/.cache}/xeneon-src
mkdir -p "$DEST"
archive="$DEST/xeneon.tar.gz"
# Only a 0.2.1 launcher understands handoff. The older loop ignores its
# argument and would start another browser if we called it here.
if [ -f "$DEST/tree/client/scripts/kiosk.sh" ] && grep -q 'handoff)' "$DEST/tree/client/scripts/kiosk.sh"; then
  sh "$DEST/tree/client/scripts/kiosk.sh" handoff || true
fi
curl -fsSL "$URL" -o "$archive"
rm -rf "$DEST/tree"
mkdir -p "$DEST/tree"
tar -xzf "$archive" -C "$DEST/tree" --strip-components=1
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
if [ -f "$CONFIG_DIR/client.json" ]; then
  echo "Keeping $CONFIG_DIR"
fi
sh "$DEST/tree/client/scripts/install.sh"
echo "Updated from $URL"
