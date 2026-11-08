#!/bin/sh
# Start the local bridge if needed, then a fullscreen browser when a display exists.
# No display (Raspberry Pi Connect SSH) leaves the bridge running and prints the URL.
# Browser crashes are restarted. Boot firmware and login settings are not changed.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
PORT=8734
if [ -f "$CONFIG_DIR/client.json" ]; then
  FOUND=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("localPort", 8734))' "$CONFIG_DIR/client.json" || true)
  if [ -n "${FOUND:-}" ]; then
    PORT=$FOUND
  fi
fi
if ! python3 -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:$PORT/health', timeout=1)" >/dev/null 2>&1; then
  PYTHONPATH=$ROOT python3 -m xeneon --config "$CONFIG_DIR/client.json" >/tmp/xeneon-bridge.log 2>&1 &
fi
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
  echo "No graphical display. Bridge URL http://127.0.0.1:$PORT/ . Raspberry Pi Connect can open that page."
  wait || true
  exit 0
fi
BROWSER=""
for candidate in chromium chromium-browser google-chrome firefox; do
  if command -v "$candidate" >/dev/null 2>&1; then
    BROWSER=$candidate
    break
  fi
done
if [ -z "$BROWSER" ]; then
  echo "No Chromium or Firefox on PATH. Open http://127.0.0.1:$PORT/ yourself."
  wait || true
  exit 0
fi
while true; do
  if [ "$BROWSER" = "firefox" ]; then
    "$BROWSER" --kiosk "http://127.0.0.1:$PORT/" ${XENEON_BROWSER_FLAGS:-} || true
  else
    "$BROWSER" --kiosk --no-first-run --disable-infobars --app="http://127.0.0.1:$PORT/" ${XENEON_BROWSER_FLAGS:-} || true
  fi
  sleep 2
done
