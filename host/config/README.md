# Host config files

`example.host.json` is strict JSON. Copy it to `host.json` or `config/local.host.json` and put sensor ids from `list-sensors` into the role fields. Leave a role null until you choose it. Null stays null in the snapshot. This file has no machine names and no tokens.

`example.shutdown.json` is the shape for a later Pi poweroff opt-in. The address `192.168.1.40` is a placeholder. Setup does not arm it. `Enable-PiShutdown.ps1` refuses that placeholder.
