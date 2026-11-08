# Code tour

This tree is three languages plus shell scripts. Nothing is minified.

| Language | Where | Role |
| --- | --- | --- |
| C# on .NET 10 | `host/` | Read sensors, optional token-checked telemetry feed, Windows preshutdown coordinator |
| Python 3 standard library | `client/xeneon/` | Poll the host, serve the page, accept only a poweroff action |
| HTML, CSS, vanilla JavaScript | `client/xeneon/static/` | Draw the 2560×720 dashboard at display rate |
| PowerShell | `host/scripts/` | Build, publish, and optional elevated service setup |
| POSIX shell | `client/scripts/` | User-level install and an optional overlay check |
| NSIS | `host/installer/` | Windows setup program. Service section is off unless selected |

## Directory map

```
docs/CODE-TOUR.md          this file
protocol/schemas/          telemetry JSON schema 1.0.0, the only copy
protocol/docs/             transport, shutdown, and later work
host/src/Diego.Telemetry.Abstractions/   shared records and the hardware source interface
host/src/Diego.Telemetry.Core/           config, units, candidates, snapshot JSON
host/src/Diego.Telemetry.Fake/           synthetic fixture used by --synthetic and tests
host/src/Diego.Telemetry.LibreHardwareMonitor/  Windows PawnIO reader, compile-time only there
host/src/Diego.Telemetry.Host/           CLI, HTTP feed, shutdown coordinator, SCM adapter
host/tests/                  dotnet test
host/config/example.host.json            sensor role map, no secrets
host/config/example.shutdown.json        Pi poweroff wait policy, no secrets
host/installer/                  NSIS setup. Product version 0.2.0. Service and firewall sections are off. Unsigned.
host/src/Diego.Telemetry.Desktop/   Avalonia settings window. Close hides it. Exit stops it.
host/src/Diego.Telemetry.Core/RoleMapper.cs   semantic roles. Board fans stay unassigned.
host/src/Diego.Telemetry.Core/PairingBroker.cs  approval plus short code. Token hash only.
client/xeneon/spotify.py         Spotify PKCE remote control. No client secret. No audio.
host/src/Diego.Telemetry.Core/SensorIdentity.cs   unique ids when LibreHardwareMonitor repeats one
client/xeneon/thresholds.py  CPU and GPU color bands
client/xeneon/contract.py    reject bad or reordered host documents
client/xeneon/session.py     connecting / live / stale / disconnected / demo
client/xeneon/bridge.py      localhost page and 1 Hz poll
client/xeneon/poweroff.py    authenticated poweroff route
client/xeneon/wifi.py        Pi Wi-Fi, or an explicit unavailable state
client/xeneon/static/        the page
client/config/example.client.json
client/tests/                python3 -m unittest
```

## Startup to a live card

1. On Windows, `diego-telemetry-host serve` or the service opens LibreHardwareMonitor read-only and publishes `GET /v1/telemetry`. The body is the schema 1.0.0 document. A bearer token from a file is required. The listener is not started by snapshot or watch.
2. On the Pi, `python3 -m xeneon` loads `client.json`, reads the telemetry token from disk, and serves `http://127.0.0.1:8734/`.
3. `bridge.py` polls the Windows URL about once a second. A slow poll does not block the page, because the browser already has `index.html`.
4. `contract.py` drops documents whose status is not `ok` but still carry a number, and drops older sequences.
5. `thresholds.py` turns the raw Celsius sample into green, yellow, orange, or red. The same function is used for CPU and GPU.
6. `session.py` builds `/api/view`. The page fetches that every 250 ms.
7. `app.js` `render` runs on `requestAnimationFrame`. Hero numbers change only when the raw sample changes. The graph line and bar heights may ease between samples. Fan icons do not spin at 0 RPM or when the tach is missing.

## Where to change things

| Change | File |
| --- | --- |
| Color band temperatures | `displayThresholds` in `client/config/example.client.json`. Logic is `client/xeneon/thresholds.py`. CSS colors are `--green`, `--yellow`, `--orange`, `--red` in `app.css`. |
| Card layout | `client/xeneon/static/app.css` positions. The stage is 2560 by 720 and `fitStage` scales it. |
| What a missing sensor looks like | `client/xeneon/static/app.js` `paintTemp`, `paintLoad`, `paintCases`. Null stays an em dash. |
| Host sensor roles | `host/config/example.host.json` after you copy ids from `list-sensors`. |
| Repeated NVIDIA load id | `host/src/Diego.Telemetry.Core/SensorIdentity.cs`. `/gpu-nvidia/0/load/3` stays GPU Memory. GPU Bus is listed under a separate id. |
| How long Windows waits for the Pi | `graceSeconds` in `host/config/example.shutdown.json`. Cap is 90 seconds in `ShutdownLimits`. |
| Poweroff authentication | `client/xeneon/poweroff.py`. Only `action` `poweroff` is accepted. |

## Commands and tests

From the repository root:

```bash
dotnet test host/Diego.Telemetry.slnx -c Release
PYTHONPATH=client python3 -m unittest discover -s client/tests -v
```

Windows host, after you create a token file yourself:

```powershell
.\diego-telemetry-host.exe serve --listen 0.0.0.0 --port 8765 --allow-lan --token-file C:\ProgramData\Xeneon\telemetry.token --config C:\ProgramData\Xeneon\host.json
```

Pi, as the desktop user, not root:

```sh
client/scripts/install.sh
```

Service install and removal are `host/scripts/install-shutdown-service.ps1` and `uninstall-shutdown-service.ps1`. They need an elevated PowerShell on Windows. They were not run in the build environment.

## Shutdown, separate from telemetry

`serve` never asks the Pi to power off. `SERVICE_CONTROL_STOP` never asks the Pi to power off. Only `SERVICE_CONTROL_PRESHUTDOWN` runs `ShutdownCoordinator`.

The coordinator is done only after the Pi status is `halt-issued` and the settle time has elapsed. An HTTP 202 or a dead connection is not enough. If the Pi is absent, the wait stops at `absentBoundSeconds` (default 5), not the full grace. Windows itself stops waiting at `dwPreshutdownTimeout`. Since Windows 10 build 15063 that default is 10 seconds, so the install must set `SERVICE_CONFIG_PRESHUTDOWN_INFO` or the SCM will continue without this program. A power cut never enters this path.

## Not implemented

- Spotify account login. Live mode says Not connected and disables the buttons. Demo mode is a labelled local preview.
- Macros and any remote shell.
- A guarantee that shutdown can be cancelled from the Windows UI after preshutdown has started.
- Proof that the service control handler has run under the SCM. The dispatcher code is present and was not executed here.
- On-device Pi boot, Wayland, or a real PawnIO sensor read.
- Enabling the overlay filesystem. `overlay-enable.sh --check` only detects the OS. `--apply` refuses anything that is not a Raspberry Pi with raspi-config or overlayroot.
