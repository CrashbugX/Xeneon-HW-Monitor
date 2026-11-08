# Power cuts

The Pi in this setup is powered from the PC supply. When that supply disappears, software on either machine may not run.

What the shutdown path can do: if Windows is shutting down through the service control manager, the host service receives preshutdown, asks the Pi to `sync` and power off, and waits a bounded time. See `protocol/docs/shutdown.md`.

What it cannot do: a SATA power cut, a held power button, or `shutdown /f` that kills the service before preshutdown finishes. Those do not wait. An overlay root makes the microSD card’s root filesystem read-only so a torn write is less likely. It does not make a power cut safe, and it does not keep logs across reboot.

`client/scripts/overlay-enable.sh --check` only prints what it detected. It does not edit `/boot`. `--apply` runs only when `/proc/device-tree/model` contains `Raspberry Pi` and `raspi-config` or the `overlayroot` package exists. It backs up `cmdline.txt` and writes `overlayroot="tmpfs:recurse=0"` into `/etc/overlayroot.local.conf` so a separate data partition is not forced read-only. It does not append `overlayroot` to the kernel command line itself. Finish that in `raspi-config` if you want the overlay. `overlay-disable.sh` restores the backup and nothing else.

Keep `client.json` and the token files on a partition you expect to stay writable, or re-copy them after enabling the overlay. Logs from the kiosk script go to `/tmp`, which is volatile.

The Pi 4 still needs a regulated 5 V supply that meets its current draw. This repository does not specify an adapter and does not describe electrical changes. The supply you use has not been recorded here.
