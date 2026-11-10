#!/bin/sh
# Install the dashboard for the desktop user. Does not edit boot files,
# does not disable security, and refuses to run as root.
set -eu
if [ "$(id -u)" -eq 0 ]; then
  echo "Run client/scripts/install.sh as the desktop user, not root." >&2
  exit 1
fi
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
CONFIG_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/xeneon
UNIT_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user
AUTOSTART=${XDG_CONFIG_HOME:-$HOME/.config}/autostart
mkdir -p "$CONFIG_DIR" "$UNIT_DIR" "$AUTOSTART"
chmod 700 "$CONFIG_DIR"
if [ ! -f "$CONFIG_DIR/client.json" ]; then
  python3 - "$ROOT/config/example.client.json" "$CONFIG_DIR/client.json" "$CONFIG_DIR" <<'PY'
import json, sys
from pathlib import Path
source, dest, config_dir = sys.argv[1:]
data = json.loads(Path(source).read_text(encoding="utf-8"))
data["tokenFile"] = str(Path(config_dir) / "host.token")
data["shutdownTokenFile"] = str(Path(config_dir) / "shutdown.token")
Path(dest).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
PY
  echo "Wrote $CONFIG_DIR/client.json. Pair from the dashboard. This script does not invent a token."
else
  echo "Kept existing $CONFIG_DIR/client.json"
fi
LABWC=${XDG_CONFIG_HOME:-$HOME/.config}/labwc
mkdir -p "$LABWC"
# Exactly one supervise line. A second autostart entry was starting a second Chromium.
LINE="sh \"$ROOT/scripts/kiosk.sh\" supervise >>/tmp/xeneon-kiosk.log 2>&1 &"
PYTHONPATH="$ROOT" python3 - "$LABWC/autostart" "$LINE" <<'PY'
import sys
from pathlib import Path
from xeneon.kioskctl import dedupe_autostart
path, line = Path(sys.argv[1]), sys.argv[2]
text = path.read_text(encoding="utf-8") if path.is_file() else ""
path.write_text(dedupe_autostart(text, line), encoding="utf-8")
PY
# The bridge is one user service. A kiosk unit would be a second supervisor.
rm -f "$UNIT_DIR/xeneon-kiosk.service"
rm -f "$AUTOSTART"/xeneon-kiosk.desktop "$AUTOSTART"/xeneon-dashboard.desktop
cat > "$UNIT_DIR/xeneon-bridge.service" <<EOF
[Unit]
Description=Xeneon localhost dashboard bridge
[Service]
Type=simple
WorkingDirectory=$ROOT
ExecStart=/usr/bin/python3 -m xeneon --config $CONFIG_DIR/client.json
Restart=on-failure
RestartSec=2
Environment=PYTHONPATH=$ROOT
[Install]
WantedBy=default.target
EOF
# The application menu resumes a closed dashboard. It is not a second autostart.
rm -f "$AUTOSTART/xeneon-kiosk.desktop"
APP_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/applications
mkdir -p "$APP_DIR"
cat > "$APP_DIR/xeneon-dashboard.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Xeneon HW Monitor
Exec=sh "$ROOT/scripts/kiosk.sh" resume
Terminal=false
Categories=Utility;
EOF
# Stop a pre-0.2.1 loop and any supervisor this app already owns.
# The command's own pid is skipped. A normal Chromium window is not.
sh "$ROOT/scripts/kiosk.sh" handoff || true
sh "$ROOT/scripts/restart-bridge.sh" --stop-only || true
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user daemon-reload || true
  systemctl --user enable --now xeneon-bridge.service || echo "User service did not start. The kiosk script will start the bridge at graphical login. loginctl enable-linger is optional and is not run here."
fi
if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
  sh "$ROOT/scripts/kiosk.sh" supervise >>/tmp/xeneon-kiosk.log 2>&1 &
fi
# The power-off helper needs root once. Later shutdowns use sudo -n and do not ask.
# This does not run the helper. A check that executed it would power the Pi off.
if [ -x /usr/local/sbin/xeneon-poweroff ] && sudo -n -l /usr/local/sbin/xeneon-poweroff >/dev/null 2>&1; then
  echo "Pi power-off helper is already installed."
else
  echo "One sudo password installs the power-off helper. It is not asked again:"
  echo "  sudo sh \"$ROOT/scripts/install-poweroff-sudo.sh\" \"$USER\""
  if sudo -n true >/dev/null 2>&1; then
    sudo -n sh "$ROOT/scripts/install-poweroff-sudo.sh" "$USER" || true
  fi
fi
# The kiosk starts unclutter itself. Install the package when apt can run without a prompt.
if ! command -v unclutter >/dev/null 2>&1; then
  echo "The pointer is hidden by the unclutter package."
  if sudo -n true >/dev/null 2>&1; then
    sudo -n apt-get update -qq || true
    sudo -n apt-get install -y unclutter || echo "unclutter did not install. Run: sudo apt-get install -y unclutter"
  else
    echo "  sudo apt-get install -y unclutter"
  fi
fi
PROJECT=$(CDPATH= cd -- "$ROOT/.." && pwd)
# A browser upload of this tree is not executable. Mark it so the next run can be ./update.
if [ -f "$PROJECT/update" ]; then
  chmod +x "$PROJECT/update"
fi
echo "Project folder: $PROJECT"
echo "Update later with: ./update"
echo "If this copy came from a browser upload, the first run is: sh update"
echo "Installed for $USER. The bridge stays on http://127.0.0.1:8734/ . Close dashboard pauses the window until you open Xeneon HW Monitor from the menu, or until the next login."
