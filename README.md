# Xeneon HW Monitor

One repository for the Windows sensor host and the Raspberry Pi dashboard. How the pieces call each other is `docs/CODE-TOUR.md`.

| Path | What it is |
| --- | --- |
| `host/` | Read-only .NET diagnostic, optional token-checked feed, Windows preshutdown coordinator. |
| `protocol/` | Shared telemetry schema `1.0.0` plus the transport and shutdown notes. |
| `client/` | Python bridge, dashboard, and Spotify remote control. No audio streaming and no macros. |
| `PROVENANCE.md` | Where the LibreHardwareMonitor and PawnIO versions came from. |
| `LICENSE` | MIT for this tree. The NuGet library has its own license. |

Fan Control keeps controlling the fans. Nothing here edits its configuration, writes fan duty, or installs a driver. The host can listen on a LAN port after you open the settings window or start the optional service. Setup does not add a firewall rule unless that optional section is checked. Pairing and the Pi dashboard are described in `docs/ONBOARDING.md`.

The PC this was shaped for is a Ryzen 7 5800X3D, an RTX 2080 SUPER with 8 GB of VRAM, 32 GB of RAM, and a Scythe Fuma 2 whose revision was not recorded. No sensor on that machine was read in the build that produced this tree.

## Build the host

The .NET 10 SDK pin is `global.json` at this root. From `host/` in PowerShell:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\publish-win-x64.ps1
```

From a Mac or Linux checkout:

```bash
dotnet test host/Diego.Telemetry.slnx -c Release
dotnet run --project host/src/Diego.Telemetry.Host -c Release -- snapshot --synthetic
```

Real hardware exits with code 3 unless the process is the Windows publish. `--synthetic` is a fixture and is marked `synthetic: true` in the JSON. How to run the Windows build, what PawnIO is, and why a 1 second poll can still bother Fan Control are in `host/README.md`.

The Windows installer build is `host/installer/README.md`. The setup file is unsigned.

The Pi page, from a checkout:

```bash
PYTHONPATH=client python3 -m xeneon --demo --local-port 8734
PYTHONPATH=client python3 -m unittest discover -s client/tests -v
```

## Shared JSON

`protocol/schemas/telemetry-snapshot-1.0.0.schema.json` is the only copy of schema 1.0.0. The host writes that shape. The future client should read it. Neither side downloads the schema at runtime.
