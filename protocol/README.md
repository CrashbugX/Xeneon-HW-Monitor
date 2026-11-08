# Telemetry protocol

Schema version **1.0.0** is the only version in this tree.

The file is `schemas/telemetry-snapshot-1.0.0.schema.json`. Its `$id` names the path it will have in `CrashbugX/Xeneon-HW-Monitor` after that repository exists. The host does not download the schema. The client must not either. Both sides use the copy in this directory.

A document is one JSON object. `watch` writes one object per line. Times are UTC. `sampledOnce` is true for a single snapshot and false for a watch series. `synthetic` is true only for the host's fixture. A published `value` of null means the reading is unmapped, missing, stale, unsupported, or an error. A fan value of 0 with status `ok` is a stop, not a missing tach.

Units on role readings are `celsius`, `percent`, `bytes`, and `rpm`. Memory and VRAM byte values are converted from LibreHardwareMonitorLib 0.9.6's binary gigabytes (2^30) and binary megabytes (2^20). `verified` is false in this version. `binding` `derived` on memory total means Used plus Available, and `sensorId` is null.

`docs/future.md` is the list of transport and macro work that is not in the host and not in the client yet.
