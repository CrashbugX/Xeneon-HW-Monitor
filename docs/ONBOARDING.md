# Onboarding

You do not export a sensor JSON file or paste a token between the PCs.

## Windows

1. Run `XeneonHwMonitorHost-0.2.0-win-x64.exe`. Leave the service section unchecked. Check the firewall section only if you want setup, which is already elevated, to add the Private-network rules below.
2. Open **Xeneon HW Monitor** from the Start menu. That opens the settings window, not a command prompt. Closing the window leaves the app running. **Exit** stops it. When the shell shows a tray icon, **Open** shows the window again. This installer does not add a Windows logon shortcut. The optional service is manual and is not started. The notification area was not exercised in the build environment.
3. Poll interval is in seconds, from 0.5 through 3600. Apply poll saves it for the next sample.
4. The window shows the telemetry, inventory, and discover URLs for the current port. Copy does not put a token in the URL. Apply port moves the listener and updates those URLs. It does not edit a firewall rule. If the new port cannot be bound, the previous port is restored and the window says so.
5. When the Pi requests a code, the window shows it. Click **Approve pairing**, then submit that code on the Pi. A request is not approved by itself. Five wrong codes or two minutes cancel it.
6. Paired devices are listed by name and last-seen time. Choose one and click **Revoke this device**. There is no id to type. Revoke deletes that device's stored token hash.
7. Pi power-off is not armed by this window or by a normal install. The optional service still has no shutdown config until you opt in later.

## Private-network firewall

The Pi sends a UDP query to discovery port 8764 unless you changed `discoveryPort`. The host answers that socket, so the Pi does not need an inbound rule for the reply. Pairing and telemetry are inbound TCP on the publish port, 8765 unless you changed it. Typing the PC address on the Pi uses that TCP port and still works when UDP is blocked. The discovery packet contains the computer name, product, version, and telemetry port. It does not contain a token. No router change is required.

Windows often blocks those inbound packets on a Private network. `Repair-PrivateFirewall.ps1` deletes this app's old rules and adds inbound TCP for the publish port and inbound UDP for the discovery port. Both are Private profile and local subnet only. Public and Domain are not opened. The firewall is not turned off.

The installer runs that script only if you check the firewall section. Later, run it yourself in an elevated PowerShell. The settings window explains why and does not open the administrator prompt. Run it again after a port change: the listener moves immediately, and the old rule still names the previous port until the script runs.

## Raspberry Pi

A browser upload does not keep the executable bit, so run the scripts with `sh`:

```sh
sh client/scripts/install.sh
```

Run that as the desktop user, not root. It keeps an existing `client.json`. It does not use a fixed `/home/pi` path. It stops a previous bridge only when that process command line is this app (`python -m xeneon`), then starts the user service. It does not close the browser or other Python programs. If a desktop is already running and the kiosk is not, it starts `sh client/scripts/kiosk.sh`. On labwc it appends `sh .../kiosk.sh` to `~/.config/labwc/autostart` once. That autostart line is what runs at the next graphical login. It was not executed on a Pi here.

If you previously started a manual demo in a terminal, run this before opening the page, from the updated tree:

```sh
sh client/scripts/restart-bridge.sh
```

That frees port 8734 when the listener is this app, then restarts `xeneon-bridge.service`. The live bridge then serves the page. Demo numbers appear only when you start the app with `--demo`.

Open `http://127.0.0.1:8734/` in the Pi's own browser. Raspberry Pi Connect's desktop is enough. **Setup** is on the dashboard.

1. **Host.** Find hosts, or type the address and port. **1. Request code**, approve it on Windows, then **2. Submit code**. The device token is stored in `~/.config/xeneon/host.token` mode 600. The page does not receive it.
2. **Layout.** Turn modules on or off. A hidden module is named in the overflow note. The 2560×720 stage stays the wide layout. Narrow and portrait windows stack the cards and scroll.
3. **Spotify.** Press **Connect Spotify**. Spotify's own page opens in this browser. Approve it there. You do not type a Client ID, a client secret, or your Spotify password. The built-in public Client ID is `c15bb4b8de984a84a831995690451695`. Advanced can replace it; a blank field returns to that id, and a saved nonempty id is left alone.

From a checkout, update with:

```sh
sh client/scripts/update-from-github.sh
```

That downloads `https://github.com/CrashbugX/Xeneon-HW-Monitor/archive/refs/heads/main.tar.gz`, keeps the config directory, and runs `sh client/scripts/install.sh`, which stops the old Xeneon bridge and starts the user service again. It does not copy files from the Windows PC.

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

Shuffle is its own on/off switch. Repeat is a separate cycle: off, all, then one. They are not the same control.

Paused playback shows **Nothing playing** and **Paused**. The title, art, and progress are hidden. Pause is offered only while something is playing. Play is offered only while paused. No active device, a Premium refusal, a rate limit, and a logged-out state leave the transport buttons disabled and name the reason. There is no volume control. The consent page itself has not been completed against Spotify yet.

## What this build did not run

The Windows notification area, a real NVAPI read of the new build, the Pi on Debian, and a live Spotify consent were not executed here. The settings window screenshot is a headless render of the Avalonia window. The dashboard screenshots are the Pi page in demo mode.
