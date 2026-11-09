#!/bin/sh
# Supervises one dashboard browser. Chromium --kiosk ignores the page's
# fullscreen API, so Exit fullscreen and Close dashboard ask this process
# to relaunch or to stay paused. Pause lasts for this login only.
# The bridge and the token files are not stopped or deleted.
# Invoke with sh. A browser upload does not keep the executable bit.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
export PYTHONPATH="$ROOT${PYTHONPATH:+:$PYTHONPATH}"
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
PORT=8734
if [ -f "$CONFIG_DIR/client.json" ]; then
  FOUND=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("localPort", 8734))' "$CONFIG_DIR/client.json" || true)
  case "${FOUND:-}" in
    ''|*[!0-9]*) ;;
    *) PORT=$FOUND ;;
  esac
fi
CMD=${1:-supervise}

case "$CMD" in
  stop-old|read-mode|handoff)
    python3 -m xeneon.kioskctl "$CMD"
    exit $?
    ;;
  resume)
    python3 -m xeneon.kioskctl set-mode fullscreen
    if ! python3 -m xeneon.kioskctl poke; then
      sh "$ROOT/scripts/kiosk.sh" supervise >>/tmp/xeneon-kiosk.log 2>&1 &
    fi
    exit 0
    ;;
esac

if [ "$CMD" != "supervise" ]; then
  echo "usage: sh client/scripts/kiosk.sh [supervise|resume|stop-old|handoff]" >&2
  exit 2
fi

RUNTIME=$(python3 -c 'from xeneon.kioskctl import runtime_dir; print(runtime_dir())')
mkdir -p "$RUNTIME"
exec 9>"$RUNTIME/supervisor.lock"
if ! flock -n 9; then
  echo "Xeneon kiosk supervisor is already running."
  exit 0
fi
printf '%s\n' "$$" > "$RUNTIME/supervisor.pid"
if [ ! -f "$RUNTIME/mode" ]; then
  printf '%s\n' "fullscreen" > "$RUNTIME/mode"
fi

kick=0
trap 'kick=1' USR1

ensure_bridge() {
  if ! python3 -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:$PORT/health', timeout=1)" >/dev/null 2>&1; then
    PYTHONPATH="$ROOT" python3 -m xeneon --config "$CONFIG_DIR/client.json" >>/tmp/xeneon-bridge.log 2>&1 &
  fi
}

owned_browser_alive() {
  pid=$1
  [ -n "$pid" ] || return 1
  [ -r "/proc/$pid/cmdline" ] || return 1
  python3 - "$pid" <<'PY'
import sys
from pathlib import Path
from xeneon.kioskctl import is_owned_browser, proc_args, profile_dir
args = proc_args(int(sys.argv[1])) or []
raise SystemExit(0 if is_owned_browser(args, profile_dir()) else 1)
PY
}

stop_owned_browser() {
  pid=$(cat "$RUNTIME/browser.pid" 2>/dev/null || true)
  if owned_browser_alive "$pid"; then
    kill -TERM "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -f "$RUNTIME/browser.pid"
}

launch_browser() {
  mode=$1
  browser=${XENEON_BROWSER:-}
  if [ -z "$browser" ]; then
    for candidate in chromium chromium-browser google-chrome firefox; do
      if command -v "$candidate" >/dev/null 2>&1; then
        browser=$candidate
        break
      fi
    done
  fi
  if [ -z "$browser" ]; then
    echo "No Chromium or Firefox on PATH. Open http://127.0.0.1:$PORT/ yourself. Close and Exit fullscreen need the kiosk launcher." >&2
    return 1
  fi
  # exec-browser replaces this python process, so the waited pid is the browser.
  python3 -m xeneon.kioskctl exec-browser "$browser" "$mode" "$PORT" &
  echo $! > "$RUNTIME/browser.pid"
  wait $! || true
}

wait_kick() {
  kick=0
  while [ "$kick" -eq 0 ]; do
    sleep 1 &
    wait $! || true
  done
  kick=0
}

ensure_bridge
while true; do
  mode=$(python3 -m xeneon.kioskctl read-mode || true)
  if [ -z "$mode" ]; then
    mode=fullscreen
  fi
  if [ "$mode" = "paused" ]; then
    stop_owned_browser
    wait_kick
    continue
  fi
  if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ] && [ -z "${XENEON_BROWSER:-}" ]; then
    echo "No graphical display. Bridge URL http://127.0.0.1:$PORT/ . Resume the window from the Xeneon menu when a desktop is open."
    wait_kick
    continue
  fi
  launch_browser "$mode" || true
  if [ "$kick" -eq 1 ]; then
    stop_owned_browser
  fi
  mode=$(python3 -m xeneon.kioskctl read-mode || true)
  if [ -z "$mode" ]; then
    mode=fullscreen
  fi
  if [ "$mode" = "paused" ]; then
    continue
  fi
  if [ "$kick" -eq 0 ]; then
    sleep 2 &
    wait $! || true
  fi
  kick=0
done
