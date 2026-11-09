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

# Pick a browser binary. Empty when this session has no graphical display.
choose_browser() {
  browser=${XENEON_BROWSER:-}
  if [ -n "$browser" ]; then
    printf '%s\n' "$browser"
    return 0
  fi
  if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    return 1
  fi
  for candidate in chromium chromium-browser google-chrome firefox; do
    if command -v "$candidate" >/dev/null 2>&1; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}

# Start one owned browser and return when its process image is that browser,
# or when the starter exits. Do not wait for the window's whole lifetime:
# Chromium can exit the starter after handing the profile to a window that
# stays open, and waiting on the starter used to launch another window.
start_browser() {
  mode=$1
  browser=$2
  python3 -m xeneon.kioskctl exec-browser "$browser" "$mode" "$PORT" &
  pid=$!
  echo "$pid" > "$RUNTIME/browser.pid"
  i=0
  while [ "$i" -lt 50 ]; do
    if ! kill -0 "$pid" 2>/dev/null; then
      return 0
    fi
    if owned_browser_alive "$pid"; then
      # A starter that only hands the profile to another window exits
      # immediately. Wait briefly so that exit is not counted as the window.
      sleep 0.2
      return 0
    fi
    sleep 0.1
    i=$((i + 1))
  done
  return 0
}

wait_kick() {
  while [ "$kick" -eq 0 ]; do
    sleep 1 &
    wait $! || true
  done
}

wait_for_pid_or_kick() {
  pid=$1
  while [ "$kick" -eq 0 ]; do
    if ! kill -0 "$pid" 2>/dev/null; then
      return 0
    fi
    sleep 1 &
    wait $! || true
  done
}

sleep_interruptible() {
  left=$1
  while [ "$left" -gt 0 ] && [ "$kick" -eq 0 ]; do
    sleep 1 &
    wait $! || true
    left=$((left - 1))
  done
}

ensure_bridge
while true; do
  kick=0
  browser=$(choose_browser || true)
  if [ -z "$browser" ]; then
    echo "No graphical display. Bridge URL http://127.0.0.1:$PORT/ . Resume the window from the Xeneon menu when a desktop is open."
    wait_kick
    continue
  fi
  # One attempt. A Chromium handoff exit must not schedule another launch.
  python3 -m xeneon.kioskctl apply "$browser" "$PORT" >/dev/null || true
  mode=$(python3 -m xeneon.kioskctl read-mode || true)
  applied=$(python3 -m xeneon.kioskctl read-applied || true)
  pid=$(python3 -m xeneon.kioskctl main-pids | awk '{print $1}')
  while [ "$kick" -eq 0 ]; do
    now=$(python3 -m xeneon.kioskctl read-mode || true)
    held=$(python3 -m xeneon.kioskctl read-applied || true)
    if [ "$now" != "$held" ]; then
      break
    fi
    if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then
      break
    fi
    if [ -z "$pid" ] && [ "$now" != "paused" ]; then
      break
    fi
    sleep 0.5 &
    wait $! || true
  done
done
