# Later, on purpose

The host can publish telemetry and, on Windows preshutdown only, ask the Pi to power off. See `transport.md` and `shutdown.md`. The pieces below are still absent.

## Transport to the Pi

`serve` is an optional cleartext GET. It is not on unless someone starts it with a token file. There is still no UDP broadcast and no firewall change. The Pi draws at display rate from the latest sample. It does not ask Windows to sample at 60 Hz.

Warnings, color changes, and hysteresis are decided on the Pi from these samples. They are not a reason to write a fan register.

## Macros

A later module can run a fixed allow-list of actions the operator wrote down, such as launching a program that is already installed. It should not take an arbitrary command string from the network. It should not be loaded by this diagnostic. Fan duty is not a macro target. Fan Control already owns that.

## What stays out of the host

- Editing Fan Control's JSON or its program folder.
- Installing or updating PawnIO.
- A Windows service, a scheduled task, or a logon item.
- WinRing0, or any other known-vulnerable driver, if a future LibreHardwareMonitor build grew one back. That would be a stop, not a silent fallback.
