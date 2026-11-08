# Later, on purpose

The host is a local diagnostic. `client/` has no implementation. The pieces below are named so they are not quietly added.

## Transport to the Pi

The JSON schema is the contract a later sender can put on the wire. The sender is not in this build. There is no socket, no HTTP listener, no UDP broadcast, and no firewall change.

When a sender is added it should be a separate project that reads the same documents and pushes them out. It should authenticate the Windows PC to the Pi, and the Pi should be the side that chooses the peer. A service that listens on the LAN is not the default. The poll period stays 1 Hz here. The Pi draws at whatever rate it wants, including 60 Hz, from the newest sample. It does not ask Windows to sample at 60 Hz.

Warnings, color changes, and hysteresis are decided on the Pi from these samples. They are not a reason to write a fan register.

## Macros

A later module can run a fixed allow-list of actions the operator wrote down, such as launching a program that is already installed. It should not take an arbitrary command string from the network. It should not be loaded by this diagnostic. Fan duty is not a macro target. Fan Control already owns that.

## What stays out of the host

- Editing Fan Control's JSON or its program folder.
- Installing or updating PawnIO.
- A Windows service, a scheduled task, or a logon item.
- WinRing0, or any other known-vulnerable driver, if a future LibreHardwareMonitor build grew one back. That would be a stop, not a silent fallback.
