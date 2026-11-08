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

## Install

```sh
client/scripts/install.sh
```

That script refuses root. It adds a user systemd service and a desktop autostart entry. It does not edit boot configuration. `uninstall.sh` removes those entries. `update.sh` reinstalls them from this tree and restarts the user service. It does not download code.

If there is no `DISPLAY` or `WAYLAND_DISPLAY`, `kiosk.sh` does not loop a browser. Raspberry Pi Connect can open the localhost URL. A missing browser is reported. Extra browser flags, if a compositor needs them, go in `XENEON_BROWSER_FLAGS`. The script does not guess Wayland versus X11.

Power-cut behavior and the optional overlay check are in `docs/POWER.md`.

## Tests

```sh
PYTHONPATH=client python3 -m unittest discover -s client/tests -v
```

Spotify is not connected. Live mode shows Not connected. Demo play, pause, shuffle, and repeat are local UI state only.
