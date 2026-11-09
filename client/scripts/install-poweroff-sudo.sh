#!/bin/sh
# Install the fixed power-off helper and a passwordless sudoers rule for it.
# Idempotent. Run once with sudo. It does not change fan control or the firewall.
set -eu
if [ "$(id -u)" -ne 0 ]; then
  echo "Pi power-off needs one sudo password. Run:" >&2
  echo "  sudo sh client/scripts/install-poweroff-sudo.sh" >&2
  exit 1
fi
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
USER_NAME=${1:-${SUDO_USER:-}}
if [ -z "$USER_NAME" ] || [ "$USER_NAME" = "root" ]; then
  echo "Pass the desktop user name: sudo sh client/scripts/install-poweroff-sudo.sh pi" >&2
  exit 1
fi
install -m 0755 "$ROOT/scripts/xeneon-poweroff" /usr/local/sbin/xeneon-poweroff
RULE="/etc/sudoers.d/xeneon-poweroff"
TMP=$(mktemp)
printf '%s\n' "$USER_NAME ALL=(root) NOPASSWD: /usr/local/sbin/xeneon-poweroff" > "$TMP"
if command -v visudo >/dev/null 2>&1; then
  visudo -cf "$TMP"
fi
install -m 0440 "$TMP" "$RULE"
rm -f "$TMP"
echo "Installed /usr/local/sbin/xeneon-poweroff for $USER_NAME. No password is asked for that command."
