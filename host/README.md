# Windows host

Read-only diagnostic. It lists sensors and writes JSON snapshots. The optional `serve` command and the Windows `service` command are described in `../docs/CODE-TOUR.md`. `serve` does not power off the Pi. Only preshutdown does, and only when the service is installed on Windows.

Fan Control stays in charge of the fan curves. This program does not edit Fan Control's configuration, write fan duty, install a driver, register a startup service, or open a network port.

## Prerequisites

1. Install the .NET 10 SDK from <https://dotnet.microsoft.com/download/dotnet/10.0>. This host targets `net10.0`. Microsoft's support policy, last updated 25 October 2026, lists .NET 10 as the active LTS release through 14 November 2028. .NET 8 and .NET 9 leave support on 10 November 2026. The build recorded in `../PROVENANCE.md` used SDK 10.0.403, which carries runtime 10.0.14.
2. Install PawnIO yourself if `check-backend` says it is missing. Official page: <https://pawnio.eu/>. Official installer releases: <https://github.com/namazso/PawnIO.Setup/releases>. The installer checked for this tree is 2.2.0, published 15 March 2026: <https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0>. This repository does not download or run that installer.
3. Fan Control V238 and later already asks for PawnIO and no longer ships WinRing0. If Fan Control is reading motherboard sensors, PawnIO is probably already installed. Confirm with `check-backend`. Do not remove or reconfigure Fan Control.
4. `XeneonHwMonitor.exe` embeds `requireAdministrator`. Windows shows a consent prompt before it starts. If a copy somehow runs without elevation, the window explains that and does not pretend the sensors opened. Do not disable antivirus, driver signature enforcement, or any other Windows security setting.

There is no WinRing0 fallback. LibreHardwareMonitorLib 0.9.6 removed that driver. If PawnIO is absent, CPU temperatures and motherboard fans are reported as errors or left unopened, with null values. They are not filled with zeroes.

## Build, test, and publish

From this `host/` directory, in PowerShell:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\publish-win-x64.ps1
```

`publish-win-x64.ps1` writes a self-contained folder to `artifacts\win-x64\`. That folder is gitignored. The program is `diego-telemetry-host.exe`. LibreHardwareMonitorLib.dll has to sit beside it because the PawnIO modules are embedded resources in that DLL. This is a folder publish, not a single-file bundle, for that reason.

On macOS or Linux, from the repository root:

```bash
dotnet test host/Diego.Telemetry.slnx -c Release
dotnet run --project host/src/Diego.Telemetry.Host -c Release -- snapshot --synthetic
```

Publishing the Windows folder from a non-Windows SDK also works, from the repository root:

```bash
dotnet publish host/src/Diego.Telemetry.Host/Diego.Telemetry.Host.csproj -c Release -r win-x64 --self-contained true -o host/artifacts/win-x64
```

A Windows exe from that publish was produced once and checked as a PE32+ x64 binary. It was not executed, and no hardware reading was taken.

## First commands on the Windows PC

```powershell
cd artifacts\win-x64
.\diego-telemetry-host.exe check-backend
.\diego-telemetry-host.exe list-sensors --output "$env:USERPROFILE\Desktop\sensors.json"
.\diego-telemetry-host.exe snapshot --output "$env:USERPROFILE\Desktop\snapshot.json"
.\diego-telemetry-host.exe watch --count 5 --output "$env:USERPROFILE\Desktop\watch.jsonl"
```

`check-backend` only reads the PawnIO uninstall registry value. It does not call `Computer.Open` and does not install anything.

`list-sensors` and `snapshot` wait one poll interval (default 1 second) before the sample they print. LibreHardwareMonitor's CPU load needs two updates. The first update can store 0 percent. This host discards that first update unless you pass `--skip-warmup`. With `--skip-warmup`, CPU load values are null and the status is `error` / `warmup_skipped`.

Copy sensor ids from `inventory` into a config file only after you have decided which row is which. Start from `config/example.host.json`. Put your copy at `config/local.host.json`. That filename is gitignored. The example file has no machine names, no account paths, and no sensor ids.

```powershell
.\diego-telemetry-host.exe snapshot --config "$env:USERPROFILE\Desktop\local.host.json"
```

A role you have not mapped stays `null` with status `unmapped`. A missing sensor stays `null` with status `missing`. A stopped fan is the number `0` with status `ok` and unit `rpm`. Those are different.

## Roles

| Config field | Unit | How it gets a value |
| --- | --- | --- |
| `cpuTemperature` | celsius | Only the id you set. Several CPU temperatures can exist at once (Tctl, Tdie, CCD). |
| `gpuTemperature` | celsius | Only the id you set. Core, hotspot, and memory junction are separate. |
| `cpuUtilizationTotal` | percent | Only the id you set. A candidate appears when the library name is exactly `CPU Total`. |
| `cpuUtilizationLogical` | percent | Ordered list of ids you set. The library's "Core # / Thread #" text is not treated as an OS logical-processor index. 16 entries are not guaranteed. |
| `gpuLoad` | percent | Only the id you set. |
| `memoryUsed` | bytes | Only the id you set, unless you opt into the unique-candidate switch below. |
| `memoryTotal` | bytes | An id if you set one. Otherwise Used plus Available, with `binding` `derived` and `sensorId` null. LibreHardwareMonitorLib 0.9.6 has no physical-total sensor. |
| `vramUsed`, `vramTotal` | bytes | Only the ids you set. `GPU Memory Used` and `D3D Dedicated Memory Used` are both candidates and are not the same counter. |
| `fanCpu`, `fanGpu`, `fanCase` | rpm | Lists of ids you set. Motherboard header names are never sorted into CPU cooler versus case. |

`--accept-unique-candidates` fills a single-value role when the inventory has exactly one candidate. `verified` is still false. Fan lists and per-thread loads are never filled that way. The default is off.

Clock sensors are listed in the inventory and are not mapped to a role.

## Fan Control and polling

Motherboard fan tachometers and a lot of board temperatures sit behind the Super I/O chip. Fan Control polls that chip too. Two readers can interfere even when both are read-only. This host defaults to one sample per second. The fastest accepted interval is 0.5 seconds, so it cannot be pointed at a 60 Hz poll. The Pi client, later, can draw at 60 Hz from the last JSON sample. Warnings and hysteresis belong on that client, not in this process.

If Fan Control's curves twitch after you start this host, run it again with `--no-motherboard-polling` or set `"motherboardPolling": false`. CPU load, GPU NVAPI sensors, and the Windows memory counters do not use that Super I/O path. CPU package temperature on the 5800X3D does use PawnIO, which is a separate driver from the Super I/O poll but is still a kernel device Fan Control also uses.

A Fuma 2 is a dual-tower cooler. If both fans share one header through a splitter that has a single tach wire, the board exposes one RPM for both fans. A reading of 0 RPM with status `ok` means the header reported a stop. A header with no tach, a failed read, or a stale sample is `null`.

## JSON

Snapshots use schema `1.0.0`. The only file is `../protocol/schemas/telemetry-snapshot-1.0.0.schema.json`. Times are UTC. `sampledOnce` is true for `snapshot` and `list-sensors`, and false for `watch`. `synthetic` is true only for `--synthetic`. Watch writes one JSON object per line.

`verified` is always false in this version. Nothing here has been checked against the physical fan header or the die sensor.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command finished, or Ctrl+C closed the hardware cleanly. |
| 2 | Bad arguments or bad config. |
| 3 | Real hardware was requested where that backend is not compiled or not running on Windows. |
| 4 | `Computer.Open` failed. |

## More detail

- `../PROVENANCE.md` — versions, licenses, and where each fact came from.
- `docs/COLLECTION.md` — what the library actually exposes, and what this host refuses to guess.
- `../protocol/docs/future.md` — the Pi transport and macros that are intentionally absent.
- `../client/README.md` — the client does not exist yet.
