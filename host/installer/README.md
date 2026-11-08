# Xeneon HW Monitor Host installer

Product name: **Xeneon HW Monitor Host**. Version **0.1.1**. The setup file is unsigned. Windows SmartScreen can warn. This project does not disable Defender, SmartScreen, or driver signature checks, and it does not install PawnIO.

Build on a machine with the .NET 10 SDK and NSIS:

```sh
host/installer/build-installer.sh
```

The script publishes `win-x64` self-contained with trimming off, runs `makensis`, then `fix-pe-checksum.py`. NSIS leaves the PE checksum at zero. The script writes the normal ImageHlp checksum. It does not sign the file. Output: `host/installer/dist/XeneonHwMonitorHost-0.1.1-win-x64.exe`. Version 0.1.0 is left in that directory when it is already there. The 0.1.1 setup is the one that keeps both NVIDIA load rows that share `/gpu-nvidia/0/load/3`.

## What setup does

- Copies the host under `C:\Program Files\Xeneon HW Monitor Host`.
- Creates `%ProgramData%\Xeneon\HW Monitor Host\config` and `logs` if needed.
- Writes `host.json` only when that file is absent. An upgrade does not replace it.
- Copies `shutdown.example.json` and does not create `shutdown.json`.
- Start menu: Diagnostics, List sensors, Snapshot, Start telemetry feed, Configuration, Logs, Uninstall.
- Does not open a port. Start telemetry feed exits until you create `telemetry.token` and an empty `config\serve.on`. Add `config\serve.lan` only if the Pi is on another PC. See `FIREWALL.txt` for the optional private-network firewall command. Setup does not run it.

The Windows service section is unchecked. If you select it, setup registers `XeneonTelemetryHost` with start type manual and a binPath of `diego-telemetry-host.exe service`. That path has no `--shutdown-config` and setup does not start the service. `sc stop` sends `SERVICE_CONTROL_STOP`. The host does not ask the Pi to power off on that control.

`Enable-PiShutdown.ps1` is the later, elevated opt-in. It refuses the example address `192.168.1.40` and a missing `shutdown.json`. An unpaired Pi is not armed by setup.

Uninstall deletes the program files and the service registration. It leaves `%ProgramData%\Xeneon\HW Monitor Host` in place, including tokens. Delete that folder yourself if you want it gone.

A power cut still does not run this service. Preshutdown only runs when Windows shuts down through the service control manager and the service was opted in.
