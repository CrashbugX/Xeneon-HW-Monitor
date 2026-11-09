# Onboarding

You do not export a sensor JSON file or paste a token between the PCs.

## Windows

1. Run `XeneonHwMonitorHost-0.3.0-win-x64.exe` once by hand. Version 0.2.0 has no updater, so this first step is still manual. Later releases use **Update now** in the host. Leave the service section unchecked. Setup adds the firewall rules and a logon task. It does not uninstall the previous copy, so settings and pairing tokens stay.
2. Open **Xeneon HW Monitor** from the Start menu. Windows asks for administrator once because the program requires it. Closing the window hides it and shows a one-time notice. The tray icon opens it again. Right-click that icon for **Open**, **Start with Windows**, **Check for updates**, and **Exit**. Exit is not on the window. The logon task starts the host hidden in the tray. It is not a Run registry key. The optional service is manual and is not started.
3. Poll interval is in seconds, from 0.5 through 3600. Apply poll saves it for the next sample.
4. The window shows the telemetry, inventory, and discover URLs for the current port. Copy does not put a token in the URL. Apply port moves the listener and updates those URLs. It does not edit a firewall rule. If the new port cannot be bound, the previous port is restored and the window says so.
5. When the Pi requests a code, the window shows it. Click **Approve pairing**, then submit that code on the Pi. A request is not approved by itself. Five wrong codes or two minutes cancel it.
6. Paired devices are listed by name and last-seen time. Choose one and click **Revoke this device**. There is no id to type. Revoke deletes that device's stored token hash.
7. **Power off the paired Pi when Windows shuts down or restarts** is on. Tray Exit, hiding the window, and signing out do not use it. A Windows shutdown or restart asks only a paired Pi, on `POST /v1/poweroff`, then waits until the Pi reports halt or the short absent bound. Client 0.4.2 creates the shutdown token, sends it when pairing finishes, and sends it again on later connections so an already-paired Pi does not pair twice. The Pi listens on port 8766 and accepts that token only from the paired PC. Setup on the Pi has **Allow PC to power off this Pi**, on by default. Fan duty is not written. One sudo password installs the helper: `sudo sh client/scripts/install-poweroff-sudo.sh`. Install prints that line if it is not installed yet.

## Private-network firewall

The Pi sends a UDP query to discovery port 8764 unless you changed `discoveryPort`. The host answers that socket, so the Pi does not need an inbound rule for the reply. Pairing and telemetry are inbound TCP on the publish port, 8765 unless you changed it. Typing the PC address on the Pi uses that TCP port and still works when UDP is blocked. The discovery packet contains the computer name, product, version, and telemetry port. It does not contain a token. No router change is required.

Windows often blocks those inbound packets on a Private network. `Repair-PrivateFirewall.ps1` deletes this app's old rules and adds inbound TCP for the publish port and inbound UDP for the discovery port. Both are Private profile and local subnet only. Public and Domain are not opened. The firewall is not turned off.

Setup adds the rules. If they are missing, the window says so and **Fix firewall** adds them again. The program is already elevated. After a port change, click it again so the rule name matches the port.

## Raspberry Pi

A browser upload does not keep the executable bit, so run the scripts with `sh`:

```sh
sh client/scripts/install.sh
```

Run that as the desktop user, not root. It keeps an existing `client.json`, the host token, and the Spotify token. It does not use a fixed home-folder path. It restarts only a process whose command is `python -m xeneon` (not `python -m xeneon.kioskctl` and not another program). On labwc it leaves one `sh .../kiosk.sh supervise` line in `~/.config/labwc/autostart` and removes the old XDG autostart desktop file. The application menu item **Xeneon HW Monitor** runs `sh client/scripts/kiosk.sh resume`. That menu item is not a second autostart. This was not executed on a Pi.

From the project folder (the one that contains `client/` and `update`), run:

```sh
sh update
```

A GitHub web upload drops the executable bit, so the first run is `sh update`, not `./update`. That downloads the current tree into that same folder and prints the folder path. `install.sh` marks `update` executable, so the next run can be `./update`. `sh client/scripts/update-from-github.sh` does the same download. The install script stops the old kiosk loop by its real script path and starts the managed one. You do not look up a process id.

If that shell is not already in the checkout, the labwc autostart line contains the path:

```sh
grep kiosk.sh "${XDG_CONFIG_HOME:-$HOME/.config}/labwc/autostart"
```

Change to the directory that contains `client/scripts`, then run the update command above. Do not type a guessed home-folder name.

The gear at the top right opens Setup, fullscreen, day or night, and Close dashboard. While that menu is open, the next click closes it. A click on empty dashboard only hides the options. A click on a menu item, or on another control such as Spotify transport, still runs that control and then closes the menu.  Close sets paused before Chromium is stopped. The supervisor finds that Chromium by its dedicated profile, including when the stored pid is already gone, and it does not start another window while paused. A second `kiosk.sh` finds `kiosk.lock` held and focuses the existing window. The gear label follows the real window: boot fullscreen says Exit fullscreen, and the first click leaves it. Chromium is not opened again for that click. The window uses the output size `wlr-randr` reports, or no forced size when that tool is absent. It is not pinned to 2560×720. A saved Chromium window size is cleared first. `wlr-randr` output is written to the window log and the mode is not changed. Optional: `sudo apt install wlr-randr`. The page logs its inner width, inner height, and device pixel ratio on the same log. The launcher starts with `--start-fullscreen`, not `--kiosk`, because kiosk mode ignores that control. If the page Fullscreen API does not match the window, the bridge tries `wlrctl` or `wtype` with F11 only. The attempt is appended to `$XDG_RUNTIME_DIR/xeneon/window.log`. Optional package, not installed by the update: `sudo apt install wtype`. The bridge, pairing, and Spotify login stay.

If the PC’s address changes, the Pi repeats discovery on UDP 8764 and tries the stored token. A host that accepts the token is used. Pairing is not repeated. A live connection has no banner. **Disconnected from PC** appears when samples stop. The keyboard button is visible only while Setup is open. Click the dim area around Setup to close it.

Day and night are a saved client setting. They do not follow the clock. Fans in Setup list every Fan sensor from the host inventory. The role stays unknown until you choose CPU, GPU, case, or other. Seen RPM values are samples, not a physical minimum or maximum. This app does not write fan duty.

The round keyboard button asks squeekboard on Wayland, or `wvkbd` if that program is installed. `sh client/scripts/keyboard-packages.sh` only prints the apt line. It does not install anything unless you pass `--install` yourself. Spotify controls are one row of icons on the card: shuffle, previous, play or pause, next, and repeat. Shuffle off and repeat off are white. Shuffle on and repeat on are green. The accessible name is the state, such as Shuffle on or Repeat one.

Open `http://127.0.0.1:8734/` in the Pi's own browser. Raspberry Pi Connect's desktop is enough. **Setup** is on the dashboard.

1. **Host.** Find hosts, or type the address and port. Click the discovered PC so Selected host shows its `http://` address. **1. Request code** uses that selected host, not the manual field. Approve it on Windows, then **2. Submit code**. If the status says the PC did not answer, discovery worked and the TCP port did not. The device token is stored in `~/.config/xeneon/host.token` mode 600. The page does not receive it.
2. **Layout.** Turn modules on or off. A hidden module is named in the overflow note. The 2560×720 stage stays the wide layout. Narrow and portrait windows stack the cards and scroll.
3. **Spotify.** Press **Connect Spotify**. Spotify's own page opens in this browser. Approve it there. You do not type a Client ID, a client secret, or your Spotify password. The built-in public Client ID is `c15bb4b8de984a84a831995690451695`. Advanced can replace it; a blank field returns to that id, and a saved nonempty id is left alone. After consent, **Return to dashboard** goes to `http://127.0.0.1:8734/`. The page also goes there on its own. The address bar is replaced with `/` so the login code is not kept. A failed consent has the same return path and does not print the code.

`sh client/scripts/restart-bridge.sh` restarts that bridge only. It does not close the dashboard window. `sh update` downloads `https://github.com/CrashbugX/Xeneon-HW-Monitor/archive/refs/heads/main.tar.gz` into the project folder that contains `update`, keeps `~/.config/xeneon`, and runs install from that folder. It prints the folder path. After that install, `./update` works. It does not copy files from the Windows PC.

Demo mode stays labelled. A live authentication failure does not switch to demo.

## Spotify app settings

This build uses the public Spotify app Client ID `c15bb4b8de984a84a831995690451695`. Connect Spotify is the whole login: Spotify's page, then Approve. The redirect registered for that app is exactly `http://127.0.0.1/spotify/callback`.

- App name and description: anything you want the consent screen to show.
- Redirect URI allowlist, exactly: `http://127.0.0.1/spotify/callback`  
  Spotify allows that loopback address without a port. This bridge sends `http://127.0.0.1:8734/spotify/callback` when the page is on port 8734. The Setup tab prints that runtime URI. Do not enter `localhost`. Do not enter the Pi's LAN address.
- The public Client ID built into this tree is `c15bb4b8de984a84a831995690451695`. Connect Spotify uses it. There is no client secret in this app and it will not ask for one.
- Development mode still limits who can authorize. The app owner needs Spotify Premium. A development app allows 5 users, added under Settings, Users Management. A user who is not allowlisted can reach the consent page and then get HTTP 403. The card says to add the listener under Users Management. A Premium refusal is a different message. Consent itself is completed on the Pi after you install this build. It was not completed from this environment.
- Playback buttons call the official player endpoints. They need the listener's Spotify Premium and an active Spotify Connect device (the Spotify app open somewhere). This dashboard does not play audio. A Premium refusal says so. A Development Mode refusal says to add the user under Users Management. Those are different messages.
- Scopes requested: `user-read-playback-state`, `user-read-currently-playing`, `user-modify-playback-state`.
- There is no public PKCE token-revoke endpoint in the Web API. Log out deletes the local token file. You can also remove the app at https://www.spotify.com/account/apps/.

The Spotify card is the album cover, cropped to fill the tall module, with the title and artist over a dark gradient. Shuffle is its own on/off switch. Repeat is a separate cycle: off, all, then one. They are not the same control.

Nothing playing shows that title and **Spotify is idle**, with no cover. A paused track keeps the cover, the title, and the progress, and the round button shows play. Pause is offered only while something is playing. Play is offered only while paused. No active device, a Premium refusal, a rate limit, and a logged-out state leave the transport buttons disabled and name the reason. There is no volume control. Spotify consent was not completed from this environment.

## What this build did not run

The Windows notification area, a real NVAPI read, and the Pi on Debian 13 / labwc / Wayland were not executed here. The window launcher was checked with a fake browser that exits after opening a window. That is not a Wayland result. The dashboard screenshots are the Pi page in demo mode.
