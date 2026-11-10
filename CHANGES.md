# Client 0.5.0

Day mode fills each CPU and GPU temperature card with its state color (green, amber, orange, or red) and uses contrasting type. The numbers stay in the 0.4.4 positions. Night cards are unchanged.

CPU load is a row of tall tracks, one per logical processor, with the percent and T00 label under each bar. GPU load, RAM, and VRAM keep the large figure, unit, bar, and sub-line. Case fans are one tile each, title on its own line. A fan that reports n/a is dim, not a warning color. The 2560×720 stage scales from the top-left so a shorter window does not clip the bottom.

The right-hand card is a pager: Spotify (the 0.4.4 card), a system clock with 12h/24h, and a Wi-Fi page. Swipe or tap the icons. The last page is remembered. The header Wi-Fi badge stays on every page. Wi-Fi actions use NetworkManager. Forget asks first. A new locked network can take a password, which is not stored in this app. If `nmcli` is missing, the page says so and leaves the buttons off.

Setup has a Weather tab. Open-Meteo needs no key. Location is automatic from the network, or a city you type. Effects (sun, clouds, rain, storm, fog) draw behind the cards and pause when the tab is hidden. Card opacity stops at 68% so the numbers stay readable. A header badge shows the condition.

The gear menu cycles Theme: Auto, Day, and Night. Auto follows sunrise and sunset from the weather location, or 07:00–19:00. Brightness is a dim overlay until a real panel backlight exists. Close dashboard asks you to confirm. The menu still closes on the next click, and a real control still runs.

`install.sh` installs the `unclutter` package when it can, and the kiosk starts it so the pointer hides. That is the Debian package, not a custom cursor hider.

# Client 0.4.4

The Spotify card matches the PrestoDeck mock. Album art cover-crops the whole 580×648 card. A color wash sampled from the art and a soft dark gradient at the bottom keep the title readable. The control row is always visible: shuffle, previous, a white circular play/pause, next, and repeat. Shuffle is the crossing arrows and repeat is the loop, white when off and Spotify green when on. Idle says Nothing playing. A paused track keeps its cover. Play is still offered only while paused, and pause only while playing.

The gear menu closes on the next click. A click that is not a control only hides the menu. A click on a menu item or another dashboard control still runs that control, and the menu closes too.

# Client 0.4.3

The temperature line is the path of the displayed head. When a reading arrives, the head eases from its current value to that reading over the measured interval (smoothstep), and the big number is the same value. The time axis is the last 30 seconds of wall-clock time, so it scrolls at a constant speed. A missed reading holds. Several readings at once glide once to the newest. The interval is still the rolling median of recent gaps.

Close looks up Chromium by the dedicated profile, including a split `--user-data-dir` flag, signals that process group, and confirms it is gone. An empty stored pid no longer skips the kill.

The window is fullscreen on the output `wlr-randr` reports. It is not forced to 2560×720 on a 1920×1080 headless output. The page still scales a 2560×720 stage, so a real panel of that size fills the screen.

The gear menu uses the launcher's fullscreen mode until the page Fullscreen API has actually changed, so boot shows Exit fullscreen and the first click leaves fullscreen.

Wi-Fi is the SSID and the bars on one row. The SYSTEM / OVERVIEW title is gone and the cards use that space.

`sh update` in the project folder runs the GitHub update and prints that folder's path. A web upload is not executable, so the first run is `sh update`. `install.sh` marks `update` executable, and later runs can be `./update`.

# Client 0.4.2

Pairing creates a shutdown token and sends it to the PC. The Pi listens on port 8766 by default. Only the paired PC's token, from that PC's address, can `POST /v1/poweroff`. Setup shows **Allow PC to power off this Pi**, on by default. An already-paired Pi sends the token again on its next connection. `sudo sh client/scripts/install-poweroff-sudo.sh` installs the helper once so later poweroffs do not ask for a password. Install prints that command if sudo is still needed.

# Host 0.3.0

Windows host 0.3.0. Client shutdown exchange is 0.4.2.

The program requires administrator. A logon task, not the Run key, starts it hidden in the tray with highest privileges. Setup adds inbound TCP 8765 and UDP 8764 for this exe on Private and Domain, local subnet, and removes them on uninstall. The window warns when the rules are missing and Fix firewall adds them again. Closing the window hides it. Exit is only in the tray menu, which also has Open, Start with Windows, and Check for updates. The first hide shows a notice.

Windows shutdown or restart, with the setting on, asks a paired Pi that has a shutdown token to power off. Tray Exit, hiding the window, and logoff do not. Discovery on UDP 8764 and the stored pairing token are unchanged.

Check for updates reads GitHub releases of CrashbugX/Xeneon-HW-Monitor. Update now downloads `XeneonHwMonitorHost-X.Y.Z-win-x64.exe` from a tag `host-vX.Y.Z`, checks the size and SHA-256 from the release notes or the `.sha256` asset, then runs the installer with `/S`. The installer overwrites the same directory and keeps settings and pairings. It does not install until that click. 0.2.0 has no updater, so installing 0.3.0 the first time is still manual. `.github/workflows/host-release.yml` builds the installer on a `host-v*` tag.

Client 0.4.2 opens that port and sends the token. No JSON edit is required.

# Client 0.4.1 temperature modules

CPU and GPU cards follow Diego's mockup. The state badge is gone. The reading and the min/avg/max line are centered. The title and the fan row keep about 22px of padding. The graph is a monotone curve with a soft fill and a dot on the newest sample. The interval is the rolling median of the gaps between sample timestamps, so it follows a poll of 0.5s, 3s, 10s, or a change from one to another. Drawing stays near 30 fps and pauses while the page is hidden.

# Client 0.4.1

The small square with black borders is an extra Chromium window, not the panel's resolution. Other Chromium windows on the same Pi were already the right size. Close used to let the supervisor start another Chromium, and that extra window came up at a saved or default size with the narrow layout, so the panel showed as a square inside black borders.

Close now writes paused before Chromium is killed. A second launcher takes `kiosk.lock` and focuses the existing window instead of starting another one. The owned window is still launched with `--window-size=2560,720` and `--window-position=0,0`, and a saved `window_placement` is cleared. `wlr-randr` is logged on launch and the output mode is not changed. The page logs `innerWidth`, `innerHeight`, and `devicePixelRatio` to the window log. Optional: `sudo apt install wlr-randr`.

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
