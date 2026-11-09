#!/bin/sh
# Download the public source archive into THIS project folder and reinstall.
# Config and tokens stay in ~/.config/xeneon. This does not copy files from
# the Windows PC and does not run as root.
#
# The folder that contains client/ is the live project. It is printed at the
# end. ~/.cache/xeneon-src is only the downloaded archive, not the copy you run.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run update as the desktop user, not root." >&2
  exit 1
fi
SCRIPT=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
PROJECT=$(CDPATH= cd -- "$SCRIPT/../.." && pwd)
URL=${XENEON_SOURCE_URL:-https://github.com/CrashbugX/Xeneon-HW-Monitor/archive/refs/heads/main.tar.gz}
CACHE=${XDG_CACHE_HOME:-$HOME/.cache}/xeneon-src
mkdir -p "$CACHE"
archive="$CACHE/xeneon.tar.gz"
if [ -f "$PROJECT/client/scripts/kiosk.sh" ] && grep -q 'handoff)' "$PROJECT/client/scripts/kiosk.sh"; then
  sh "$PROJECT/client/scripts/kiosk.sh" handoff || true
fi
curl -fsSL "$URL" -o "$archive"
stage=$(mktemp -d)
tar -xzf "$archive" -C "$stage" --strip-components=1
# Copy the new tree over this project. Do not delete .git if this is a checkout.
# Do not touch the home config directory.
cp -a "$stage"/. "$PROJECT"/
rm -rf "$stage"
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
if [ -f "$CONFIG_DIR/client.json" ]; then
  echo "Keeping $CONFIG_DIR"
fi
sh "$PROJECT/client/scripts/install.sh"
echo "Project folder: $PROJECT"
echo "Update later with: ./update   (the first run from a browser upload is: sh update)"
echo "Updated from $URL"
