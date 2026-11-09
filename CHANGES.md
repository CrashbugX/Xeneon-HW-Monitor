# Client 0.2.1

Client-only patch against the 0.2.0 tree (`4dfdc64`). The Windows host and its installer are unchanged. No installer is part of this update.

## New

- `client/xeneon/kioskctl.py` — owned-browser commands, pid checks, pause file, Spotify return page
- `client/tests/test_kiosk.py` — action auth, origin, pid ownership, window mode, pause, resume, launchers, tokens, callback

## Changed

- `client/scripts/kiosk.sh` — one supervisor instead of the `while true` kiosk loop
- `client/scripts/install.sh` — one labwc line, menu shortcut, stops the old launcher
- `client/scripts/restart-bridge.sh` — restarts `python -m xeneon` only
- `client/scripts/update-from-github.sh` — stops an owned supervisor before replacing the cached tree
- `client/scripts/uninstall.sh` — stops the owned window and removes the labwc line and menu item
- `client/xeneon/__init__.py` — client version 0.2.1
- `client/xeneon/bridge.py` — `POST /api/window` and the Spotify return page
- `client/xeneon/static/index.html` — Exit fullscreen, Fullscreen, Close dashboard
- `client/xeneon/static/app.js` — those actions, and no claim that a window closed while it is still open
- `client/xeneon/static/app.css` — fixed window bar
- `client/README.md` — launcher notes
- `docs/ONBOARDING.md` — Pi section
- `CHANGES.md` — this file

## Deleted

None.

## Behavior

Exit fullscreen relaunches the owned Chromium without `--kiosk`. Fullscreen puts `--kiosk` back. Close dashboard sets a pause for this login so the two-second loop cannot reopen the window. The bridge, pairing, and Spotify files stay. The next login starts fullscreen again. Reopen from the **Xeneon HW Monitor** menu item.

The Spotify callback is a Return to dashboard page. It also navigates to `/` and removes the code from the address bar. Errors use that same page.

Updating with the existing `sh client/scripts/update-from-github.sh` runs the new install script, which stops the old `kiosk.sh` loop when that process argument is a real `scripts/kiosk.sh` file. It does not stop a normal Chromium window.
