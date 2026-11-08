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
if [ -f "$LABWC/autostart" ] && grep -q "xeneon-kiosk.sh" "$LABWC/autostart"; then
  echo "labwc autostart already starts the kiosk."
else
  printf '\n%s\n' "sh \"$ROOT/scripts/kiosk.sh\" &" >> "$LABWC/autostart"
fi
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
cat > "$AUTOSTART/xeneon-kiosk.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Xeneon HW Monitor
Exec=sh $ROOT/scripts/kiosk.sh
X-GNOME-Autostart-enabled=true
EOF
sh "$ROOT/scripts/restart-bridge.sh" --stop-only || true
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user daemon-reload || true
  systemctl --user enable --now xeneon-bridge.service || echo "User service did not start. The kiosk script will start the bridge at graphical login. loginctl enable-linger is optional and is not run here."
fi
if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
  if ! tr '\0' ' ' < /proc/[0-9]*/cmdline 2>/dev/null | grep -q "kiosk.sh"; then
    sh "$ROOT/scripts/kiosk.sh" >/tmp/xeneon-kiosk.log 2>&1 &
  fi
fi
echo "Installed for $USER. Open http://127.0.0.1:8734/ . A previous manual demo on that port was stopped only if its command line was this app."
