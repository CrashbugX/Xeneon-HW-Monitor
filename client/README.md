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

Client 0.4.3 keeps a single Chromium. Close writes a paused flag and stops every process of the dedicated profile, then checks that the window is gone. It does not open another one. The window follows the output `wlr-randr` reports (`XENEON_OUTPUT_SIZE`), with `--start-fullscreen` on that output. It is not forced to 2560×720. `wlr-randr` is only logged. The output mode is not changed. `sudo apt install wlr-randr` makes that log possible. The page records `innerWidth`, `innerHeight`, and `devicePixelRatio` in the window log. The gear menu reads the real window: a boot that fills the screen says Exit fullscreen, and the first click leaves fullscreen. While that menu is open, the next click closes it. A control still does its action.

The live project folder is the one that contains `client/` and `update`. A GitHub web upload drops the executable bit, so the first run from that folder is `sh update`. That downloads the current GitHub tree into the same folder and reinstalls. `install.sh` marks `update` executable, so the next time `./update` works. `~/.cache/xeneon-src` only holds the downloaded archive. Config and tokens stay in `~/.config/xeneon`.

## Install

```sh
sh client/scripts/install.sh
```

That script refuses root. It adds a user systemd service and one labwc autostart line, and it removes the old XDG autostart file. The menu item resumes a closed dashboard. It does not edit boot configuration. `sh client/scripts/uninstall.sh` stops the owned dashboard window and removes those entries. Tokens stay unless you pass `--purge-config`. `sh client/scripts/update.sh` reinstalls from this tree. `sh update` (or `sh client/scripts/update-from-github.sh`) downloads the public archive into this project folder and then runs install. It prints the project folder path. After install, `./update` works. Client version is 0.5.0. Setup has a Weather tab (Open-Meteo, no key). The right card swaps among Spotify, a system clock, and Wi-Fi. `install.sh` asks for the `unclutter` package so the kiosk can hide the pointer.

`sh client/scripts/kiosk.sh supervise` owns one browser in a private profile. The dashboard button says Fullscreen or Exit fullscreen from the mode that launcher last started. Repeating it does not open another window. Close dashboard pauses it for this login. The bridge keeps running. If there is no display and `XENEON_BROWSER` is unset, it does not launch a browser. A missing Chromium or Firefox is reported. The script does not pass extra flags through the shell and does not guess Wayland versus X11.

Power-cut behavior and the optional overlay check are in `docs/POWER.md`.

## Tests

```sh
PYTHONPATH=client python3 -m unittest discover -s client/tests -v
```

Spotify is not connected. Live mode shows Not connected. The card is full-bleed album art with the five transport icons on one row. Demo play, pause, shuffle, and repeat are local UI state only.
