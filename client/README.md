# Raspberry Pi client

Local dashboard for a Pi 4. Python serves the page on `127.0.0.1`. The page is HTML, CSS, and vanilla JavaScript. It does not use a framework and it is not minified.

The map of the code is `../docs/CODE-TOUR.md`.

## Run it by hand

From the repository root, with the desktop user’s account:

```sh
PYTHONPATH=client python3 -m xeneon --demo --local-port 8734
```

Open `http://127.0.0.1:8734/`. The amber banner says the numbers are examples. Demo does not contact Windows and does not listen for poweroff.

Live mode, after you copy `config/example.client.json` and create token files yourself:

```sh
PYTHONPATH=client python3 -m xeneon --config "$HOME/.config/xeneon/client.json"
```

`controlAllowLan` stays false in the example, so the poweroff port does not bind every interface until you set it. The telemetry token and the shutdown token are different files. Neither is written into the page.

Client 0.4.1 keeps a single Chromium. Close writes a paused flag before the window is killed, and a second launcher focuses the existing window instead of starting another one. The small square with black borders was that extra window, opened without the panel size. The launcher passes `--window-size=2560,720` and `--window-position=0,0` and drops a saved `window_placement`. `wlr-randr` is only logged, on every launch, and the output mode is not changed. `sudo apt install wlr-randr` makes that log possible. The page also records `innerWidth`, `innerHeight`, and `devicePixelRatio` in the window log.

## Install

```sh
sh client/scripts/install.sh
```

That script refuses root. It adds a user systemd service and one labwc autostart line, and it removes the old XDG autostart file. The menu item resumes a closed dashboard. It does not edit boot configuration. `sh client/scripts/uninstall.sh` stops the owned dashboard window and removes those entries. Tokens stay unless you pass `--purge-config`. `sh client/scripts/update.sh` reinstalls from this tree. `sh client/scripts/update-from-github.sh` downloads the public archive and then runs install. Client version is 0.4.0.

`sh client/scripts/kiosk.sh supervise` owns one browser in a private profile. The dashboard button says Fullscreen or Exit fullscreen from the mode that launcher last started. Repeating it does not open another window. Close dashboard pauses it for this login. The bridge keeps running. If there is no display and `XENEON_BROWSER` is unset, it does not launch a browser. A missing Chromium or Firefox is reported. The script does not pass extra flags through the shell and does not guess Wayland versus X11.

Power-cut behavior and the optional overlay check are in `docs/POWER.md`.

## Tests

```sh
PYTHONPATH=client python3 -m unittest discover -s client/tests -v
```

Spotify is not connected. Live mode shows Not connected. Demo play, pause, shuffle, and repeat are local UI state only.
