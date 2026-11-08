# Changes since the previous source upload

Personal GitHub commit `9004521b90aa3eb2e1d0126db6878cbda4fe31e3` is not an object in this clone. This file is the delta to upload with the flat source zip: the sensor-identity fix and installer 0.1.1. Installer 0.1.0 is unchanged.

## list-sensors no longer dies on `/gpu-nvidia/0/load/3`

LibreHardwareMonitorLib 0.9.6 activates two NVIDIA load sensors with that one id. `NvidiaGpu` always constructs `GPU Memory` at load index 3, and NVAPI utilization domain 3 is `GPU Bus`. Both stay in the hardware list after `Update`. `list-sensors` called `ToDictionary` on the id and threw `ArgumentException` before it created the JSON file.

`SensorIdentity` runs before that dictionary, on every `list-sensors`, `snapshot`, `watch`, and `serve` sample:

- `GPU Memory` keeps `/gpu-nvidia/0/load/3`. That is the id already used when the extra row is absent. It is not whichever row was collected first.
- `GPU Bus` is published as `/gpu-nvidia/0/load/3/gpu-bus`.
- Both stay percent. A power-topology row that 0.9.6 types as Load stays percent too.
- `/gpu-nvidia/0` and `/gpu-nvidia/1` are separate.
- If the rows cannot be told apart, neither value is published. The status is `error`, code `ambiguous_sensor_identity`. An `unresolved-N` suffix only lets the file be written.
- An empty library id is `missing_library_id` and does not throw.
- The `identity` array and `backend.warnings` name the published ids. This is not a PawnIO fault. Nothing in the message asks for a driver change. The library pin stays 0.9.6.

## Installer 0.1.1

Unsigned. PE checksum `0x01A6B1E2`, matching `pefile` `generate_checksum`. `osslsigncode verify` no longer reports an invalid checksum. It still reports no signature.

The single file is 27,654,932 bytes, SHA-256 `34b0df981a17046c584283ecf5a0ee67c21c7e9ba48ce74bb5fba195abfe8d7e`. It is not in the artifact store. Concatenate:

```sh
cat \
  XeneonHwMonitorHost-0.1.1-win-x64.part001 \
  XeneonHwMonitorHost-0.1.1-win-x64.part002 \
  XeneonHwMonitorHost-0.1.1-win-x64.part003 \
  XeneonHwMonitorHost-0.1.1-win-x64.part004 \
  > XeneonHwMonitorHost-0.1.1-win-x64.exe
sha256sum XeneonHwMonitorHost-0.1.1-win-x64.exe
```

| Part | Bytes | SHA-256 |
| --- | ---: | --- |
| `XeneonHwMonitorHost-0.1.1-win-x64.part001` | 8000000 | `bd26220604d9b4de9ab66079c434e88003ab9d4ae89d1b5ca37d81d3461d82d5` |
| `XeneonHwMonitorHost-0.1.1-win-x64.part002` | 8000000 | `b902f101fb4a9fb369892f56fe886526ebb2af289e8ee208c47f58088c69fac5` |
| `XeneonHwMonitorHost-0.1.1-win-x64.part003` | 8000000 | `9d8779f31690fa9fbd3c684dcabb5453c7605296f0f81a6a88d4c7ca29cc4ba7` |
| `XeneonHwMonitorHost-0.1.1-win-x64.part004` | 3654932 | `fdb2eace34ae1f9b04eee7fb0d8ac87652482997adef66fbc0efc558021e6d91` |

`host/installer/build-installer.sh` still runs `fix-pe-checksum.py` after NSIS and now takes the version from `XeneonHost.nsi`.

## Retest on the Windows PC

1. Reassemble the exe and check the SHA-256 above.
2. Run setup over the existing 0.1.0 install. It should leave `%ProgramData%\Xeneon\HW Monitor Host` in place, including `config\host.json` and any token file.
3. Leave the service section unchecked.
4. In an elevated PowerShell:

```powershell
cd "C:\Program Files\Xeneon HW Monitor Host"
.\diego-telemetry-host.exe check-backend
.\diego-telemetry-host.exe list-sensors --output "$env:TEMP\xeneon-sensors.json"
```

`list-sensors` should exit without `ArgumentException` and the file should exist. In `inventory`, `/gpu-nvidia/0/load/3` is `GPU Memory` and `/gpu-nvidia/0/load/3/gpu-bus` is `GPU Bus`, both percent. Copy the id you want into `gpuLoad`. Do not point `gpuLoad` at the bus row if you wanted core load. `GPU Core` is a different id, usually `/gpu-nvidia/0/load/0`.
