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
  cp "$ROOT/config/example.client.json" "$CONFIG_DIR/client.json"
  echo "Wrote $CONFIG_DIR/client.json. Set hostUrl, then create the token files yourself."
fi
if [ ! -f "$CONFIG_DIR/telemetry.token" ]; then
  echo "Create $CONFIG_DIR/telemetry.token with one line and mode 600. This script does not invent a token."
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
Exec=$ROOT/scripts/kiosk.sh
X-GNOME-Autostart-enabled=true
EOF
chmod 755 "$ROOT/scripts/kiosk.sh"
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user daemon-reload || true
  systemctl --user enable --now xeneon-bridge.service || echo "User service did not start. The kiosk script will start the bridge at graphical login. loginctl enable-linger is optional and is not run here."
fi
echo "Installed for $USER. Open http://127.0.0.1:8734/ if you are testing over Raspberry Pi Connect."
