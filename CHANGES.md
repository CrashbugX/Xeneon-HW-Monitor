# Client 0.4.0

Against published client 0.3.0 (`349f4e8`). The Windows host stays 0.2.0. No installer.

## New

None beyond the files below that are new in this tree since 0.3.0 was published. `fans.py`, `keyboard.py`, and `keyboard-packages.sh` shipped in 0.3.0.

## Changed

- `client/xeneon/kioskctl.py` — fullscreen no longer relaunches Chromium. Optional F11 via `wlrctl` or `wtype` is logged.
- `client/scripts/kiosk.sh` — starts `--start-fullscreen`, not `--kiosk`, and leaves a living window alone
- `client/xeneon/bridge.py` — rediscover the paired PC with the stored token; album-art proxy
- `client/xeneon/discover.py` — choose a discovered host that accepts the token
- `client/xeneon/setupflow.py` — saved PC name, module order
- `client/xeneon/spotify.py` — account display name when Spotify returns it
- `client/xeneon/static/index.html` — setup tabs, host steps, layout list, no footer or close button
- `client/xeneon/static/app.css` — day theme on modules and setup, Spotify card, tabs
- `client/xeneon/static/app.js` — fullscreen in the page, 30–95 °C axis, disconnected notice, layout drag, Spotify card
- `client/xeneon/__init__.py` — 0.4.0
- `client/README.md`
- `client/tests/test_kiosk.py`
- `client/tests/test_pairing.py`
- `docs/ONBOARDING.md`
- `CHANGES.md`

## Deleted

None.

## Behavior

Fullscreen and Exit fullscreen use the browser Fullscreen API from the button click. Chromium is not started again. The launcher uses `--start-fullscreen` instead of `--kiosk`, because kiosk mode ignores that API. If the page reports no change, the bridge tries `wlrctl` then `wtype` with only F11, and appends a line to `$XDG_RUNTIME_DIR/xeneon/window.log`. Close dashboard still pauses the owned window and leaves the bridge, pairing, and Spotify login running.

Day mode recolors the page, panels, inputs, threads, meters, transport buttons, and setup. The saved theme is on the page before first paint, so day does not fade through the night palette. Cards stay white. Readings, state pills, graph lines, thread bars, and meters use saturated green, amber, orange, red, and blue with dark type. The temperature graph is fixed from 30 °C to 95 °C and prints 95°, 60°, and 30° beside the grid. A live link has no banner. Disconnected, stale, and refused tokens show “Disconnected from PC”.

The Spotify module is one rounded card. Album art fills the card, a dark gradient keeps the type readable, and the page samples the art for a darkened tint. The green Spotify mark sits at the top left. Title, artist, a thin progress bar, previous, a light rounded play/pause square, and next sit above smaller shuffle and repeat squares. Shuffle on and repeat (including repeat one) use the light square; off stays dim. Nothing playing keeps the same card with that title and a play icon. Demo art is a local picture, not a Spotify URL.

If the PC stops answering, the Pi repeats UDP discovery on port 8764 and uses the stored token on a host that accepts it, preferring the saved computer name. The token is not cleared and pairing is not repeated. Host 0.2.0 already serves discovery and bearer telemetry, so no host change is required.

The keyboard button is only visible while Setup is open. Clicking the dim area around Setup closes it. Host is split into Find hosts and Manual address, with pairing steps 1, 2, and 3. Layout is a drag list. Spotify shows Connect or Log out, not both, plus the account name when Spotify’s profile call succeeds.

## Dependencies

Optional, not installed by update:

```sh
sudo apt install wtype
```

`wlrctl` is the other fullscreen fallback if it is already packaged. `sh client/scripts/keyboard-packages.sh` still only prints the `wvkbd` line.

## Not run

The Pi on labwc Wayland and the Windows PC were not used. Chrome on this machine was used only to see that a second launch says “Opening in existing browser session”.
