# Xeneon HW Monitor

One repository for a Windows sensor host and a later Raspberry Pi client. The host is the part that exists. The client is a placeholder.

| Path | What it is |
| --- | --- |
| `host/` | Read-only .NET diagnostic. Lists sensors and writes JSON. Does not control fans. |
| `protocol/` | Shared telemetry schema `1.0.0` and the notes both sides have to follow. |
| `client/` | Empty on purpose. No Pi UI, no network code, no install steps that do anything. |
| `PROVENANCE.md` | Where the LibreHardwareMonitor and PawnIO versions came from. |
| `LICENSE` | MIT for this tree. The NuGet library has its own license. |

Fan Control keeps controlling the fans. Nothing here edits its configuration, writes fan duty, installs a driver, or opens a port.

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

## Shared JSON

`protocol/schemas/telemetry-snapshot-1.0.0.schema.json` is the only copy of schema 1.0.0. The host writes that shape. The future client should read it. Neither side downloads the schema at runtime.
