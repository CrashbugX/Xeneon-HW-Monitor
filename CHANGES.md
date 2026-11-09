# Client 0.3.0

Against the published Pi client 0.2.2. The Windows host stays 0.2.0. No new installer.

Unpublished 0.2.3 is included: Request code says the PC did not answer, and names the selected-host URL, instead of the raw word `pair_begin`.

## New

- `client/xeneon/fans.py` — fan list, roles, observed range
- `client/xeneon/keyboard.py` — local on-screen keyboard toggle
- `client/scripts/keyboard-packages.sh` — documents `wvkbd`; does not run on install
- `client/tests/test_pairing.py` — begin errors and the host begin JSON
- `client/tests/test_fans_gpu.py` — GPU Core fallback, fan limits, theme, keyboard action

## Changed

- `client/xeneon/kioskctl.py` — one owned window; a Chromium handoff exit is not another launch
- `client/scripts/kiosk.sh` — the supervisor calls that one attempt, then waits
- `client/xeneon/session.py` — GPU temperature can come from one inventory GPU Core when the role is empty
- `client/xeneon/setupflow.py` — theme, fan prefs, pairing failure text
- `client/xeneon/bridge.py` — theme, fan board, keyboard route
- `client/xeneon/static/index.html` — gear menu, fan tab, no top button bar
- `client/xeneon/static/app.css` — day theme, gear, keyboard button
- `client/xeneon/static/app.js` — gear, icons, fans, theme, keyboard
- `client/xeneon/__init__.py` — 0.3.0
- `client/README.md`
- `client/tests/test_kiosk.py` — handoff does not spawn twice
- `docs/ONBOARDING.md`
- `CHANGES.md`

## Deleted

None.

## Host

No host change. GPU Core is already in the 0.2.0 inventory when the semantic role is empty. Fan sensors are already in that inventory. Pairing HTTP to a 0.2.0 listener still begins, approves, and stores a token.

## Not tested on the Pi or the Windows PC

Labwc, Wayland, and the real Chromium package on the Pi were not run. A Chrome build here exits a second launch with "Opening in existing browser session" and leaves the first window. This machine's Chrome wrapper also injects another profile directory, so that wrapper is not treated as the owned window. A sensor-poll failure that stops the listener was reproduced only in a local listener process, and it was not the cause of the earlier empty port: the host window had been closed.
