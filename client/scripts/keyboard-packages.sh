#!/bin/sh
# Optional. Not run by install or update. Prints the package and installs it
# only when you pass --install yourself.
set -eu
echo "On-screen keyboard package: wvkbd"
echo "sudo apt install wvkbd"
if [ "${1:-}" = "--install" ]; then
  sudo apt install wvkbd
fi
