# Raspberry Pi client

This directory is a placeholder. There is no client program, no UI, no installer, and no network code.

The Windows host in `../host/` already writes telemetry JSON. A later client can read those documents and draw them. Drawing at 60 Hz is a client job. Sampling stays at 1 Hz on Windows. Warnings and hysteresis belong here, not in the host, and they are not a reason to write a fan register.

The contract to implement against is `../protocol/schemas/telemetry-snapshot-1.0.0.schema.json`. Read `../protocol/README.md` before adding code. Do not invent a second schema beside that file.

Nothing in this folder should be run. There is nothing to run.
