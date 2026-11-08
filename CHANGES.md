# Upload manifest

Copy the contents of the source zip onto `https://github.com/CrashbugX/Xeneon-HW-Monitor`. The zip root is the repository root. There is no extra wrapper folder.

Languages in this tree: C# (.NET 10) for the Windows host, Python 3 standard library for the Pi bridge, HTML/CSS/vanilla JavaScript for the dashboard, PowerShell and POSIX shell for scripts.

## New

- `client/xeneon/` dashboard bridge, temperature bands, document checks, Wi-Fi probe, poweroff route
- `client/xeneon/static/` 2560×720 page
- `client/scripts/` install, uninstall, update, kiosk, optional overlay check, fixed poweroff helper
- `client/config/example.client.json` and a narrow sudoers example
- `client/docs/POWER.md`
- `host` `serve` token-checked `GET /v1/telemetry`
- `host` `ShutdownCoordinator` and Windows SCM adapter for `SERVICE_CONTROL_PRESHUTDOWN`
- `host/config/example.shutdown.json`
- `host/scripts/install-shutdown-service.ps1` and `uninstall-shutdown-service.ps1` (not executed here)
- `protocol/docs/transport.md` and `protocol/docs/shutdown.md`
- `docs/CODE-TOUR.md`
- `client/tests/test_client.py` and `host/tests/.../TransportAndShutdownTests.cs`

## Updated

- Root `README.md` only enough to point at the code tour and the Pi commands
- `host/README.md`, `client/README.md`, `protocol/docs/future.md`
- File-purpose comments on the existing host sources

## Not in this upload

- `.git`, `bin`, `obj`, `__pycache__`, token files, or the screenshot PNGs
- A claim that the Windows service or the Pi boot path has been run on the real machines

Screenshots from this environment, not from a Pi:

- `/opt/cursor/artifacts/client-connecting-2560x720.png`
- `/opt/cursor/artifacts/client-demo-2560x720.png`
- `/opt/cursor/artifacts/client-disconnected-2560x720.png`
