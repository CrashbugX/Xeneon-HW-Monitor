# Client 0.2.2

Delta against client 0.2.1. The Windows host and its installer are unchanged.

## New

None.

## Changed

- `client/xeneon/kioskctl.py` — one window transition at a time, acknowledged only when the owned launcher is in that mode
- `client/scripts/kiosk.sh` — adopt a window whose starter process already exited, instead of opening another
- `client/xeneon/bridge.py` — `GET /api/window` reports that launcher state
- `client/xeneon/static/index.html` — one fullscreen toggle, Close dashboard separated
- `client/xeneon/static/app.js` — buttons stay off until the launcher ack, including after a reload
- `client/xeneon/static/app.css` — gap before Close dashboard, including a narrow row
- `client/xeneon/__init__.py` — client version 0.2.2
- `client/tests/test_kiosk.py` — repeated clicks, pending ack, labels, and the extra-window loop
- `client/README.md` — launcher note
- `docs/ONBOARDING.md` — Pi controls
- `CHANGES.md` — this file

## Deleted

None.

## Behavior

Fullscreen while the owned window is already fullscreen does not launch another Chromium. A starter process that exits and leaves the dashboard window open is adopted. Further clicks, including from an older tab, do not queue another launch while that change is unfinished. The toggle and Close dashboard stay disabled until the launcher records the mode it actually started, or until that change times out and the last started mode is kept. The button says **Exit fullscreen** after a fullscreen ack and **Fullscreen** after a windowed ack.
