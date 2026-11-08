# What this host will and will not collect

The Windows backend is LibreHardwareMonitorLib 0.9.6, opened with CPU, GPU, and memory enabled. Storage, network, battery, PSU, power-monitor, and controller groups stay disabled. Controller groups are where extra fan-controller devices live, and several of them also expose duty controls. Leaving them closed keeps this process off those devices.

Motherboard and Super I/O devices are opened only when both of these are true: `motherboardPolling` is true, and `PawnIo.IsInstalled` is true. If the driver is missing, that group is not opened. There is no second attempt through WinRing0.

The host never sets a sensor name and never writes `IControl`. `Computer.Close` runs when the command finishes and when Ctrl+C cancels a watch.

## Intervals

The default period is 1 second. Config and `--interval` reject anything under 0.5 seconds or over 3600 seconds. A snapshot waits one period between two library updates, then prints the second update. Watch does that once, then waits the period between later samples. That wait is what makes CPU load a real delta instead of the library's first zero. It is not a 60 Hz poll.

`staleAfterSeconds` defaults to 5 and must be at least as long as the poll interval. A sample whose timestamp is older than that becomes status `stale` and value `null`. A temperature or an RPM that simply holds still is not stale. Stale means the sample was not refreshed, or the source marked it stale.

## CPU temperature

On the family of CPUs implemented by `Amd17Cpu`, temperature comes from an SMN read through a PawnIO module. The 5800X3D is a Zen 3 part. The library's model switch treats model `0x21` as Zen 3 and can activate `Core (Tctl/Tdie)` plus per-CCD `Tdie` sensors. Which of those rows exist is decided by the library at runtime. This host does not hardcode `/amdcpu/0/temperature/0` or any other index, and it does not pick Tctl over CCD.

If PawnIO is not installed, every CPU temperature in the sample is status `error`, code `pawnio_not_installed`, value `null`. The library's zero buffer would otherwise show up as 0 C.

If PawnIO is installed and the library still reports exactly 0 C, the status is `error`, code `zeroed_driver_buffer`, value `null`. `libraryValue` still shows 0 so the raw report is visible. A genuine 0 C CPU would be hidden by the same guard. That is deliberate.

## CPU load

`CpuLoad` asks Windows for processor performance times through `NtQuerySystemInformation`. It does not use PawnIO. `GenericCpu` creates a sensor named exactly `CPU Total` only when the CPUID core count is greater than one. It creates `CPU Core Max` next to it. Per-thread sensors are named from CPUID core and thread numbers, for example `CPU Core #1 Thread #1`. The thread index inside the library is the CPUID thread id. This host does not treat that number as the operating system's logical-processor index.

If the performance query fails, those load sensors are absent. A 16-thread CPU is not promised 16 load rows. Map only the rows `list-sensors` actually printed.

`CpuLoad.Update` returns without computing a delta when the time window is shorter than its internal minimum. `GenericCpu.Update` still assigns `GetThreadLoad`, which is still 0 on that first call. A single immediate read can therefore publish 0 percent that was never measured. The default warmup exists so the published sample is the second update. `--skip-warmup` publishes CPU load as `error` / `warmup_skipped` and value `null` instead of that zero.

0 percent after a real interval is a legal idle reading and stays 0.

## GPU

The NVIDIA path in this library version talks to NVAPI. It is not the PawnIO module path. An RTX 2080 SUPER can expose more than one temperature (`GPU Core`, `GPU Hot Spot`, `GPU Memory Junction`) and more than one load (core, memory, video engines, D3D nodes). All of them are candidates. None is selected for you.

LibreHardwareMonitorLib 0.9.6 builds a sensor id from the hardware path, the sensor type, and the index. `NvidiaGpu` always creates the load row named `GPU Memory` at index 3. It also creates one load row per present NVAPI utilization domain, and domain 3 is named `GPU Bus`. Both are activated in the constructor, so a flattened inventory can contain two sensors whose library id is `/gpu-nvidia/0/load/3`. `Update` writes values onto those objects. It does not remove either, and a later poll does not add a third copy. `list-sensors` used to throw `ArgumentException` from `ToDictionary` before it wrote the JSON file. The host now keeps `GPU Memory` on that bare id, because that is the id a card already uses when the extra row is absent, and publishes each other distinct name at `{library id}/{name}`. `GPU Bus` becomes `/gpu-nvidia/0/load/3/gpu-bus`. Both stay percent. Rows that share a name are not given either value. They are status `error`, code `ambiguous_sensor_identity`, and the `unresolved-N` suffix is only there so the file can be written. `/gpu-nvidia/0` and `/gpu-nvidia/1` are separate. This collision is not a PawnIO detection failure. `check-backend` already reports the installed `DisplayVersion` and does not upgrade the driver.

Fan rows are `SensorType.Fan`. Control rows are percent duty. Duty is inventoried and is not a fan-RPM candidate, and it is never written.

`NvAPI_GPU_GetTachReading` is how the library asks for GPU fan speed. A returned 0 with a non-null value is status `ok`. A null value is status `missing`.

Memory counters from this library include `GPU Memory Used`, `GPU Memory Total`, `GPU Memory Free`, and `D3D Dedicated Memory Used`, as `SmallData`. In 0.9.6, `SmallData` is binary megabytes, 2^20 bytes. This host converts that to bytes and rounds to the nearest byte. `GPU Memory Used` and `D3D Dedicated Memory Used` stay as two candidates. The name "Dedicated" is not treated as proof.

## Memory

`TotalMemory` creates `Memory Used` and `Memory Available` as `Data` sensors. `MemoryWindows` fills them from `GlobalMemoryStatusEx`, divided by 2^30, stored as a float. There is no total sensor. When `memoryTotal` is left null, this host adds the two normalized byte values and marks the result `binding: derived`, `sensorId: null`, `verified: false`. The float division means the byte figure is not exact to the byte. If either half is missing, the total is null and the status is `missing`. If the names are duplicated, the status is `error` and no sum is produced.

Virtual-memory sensors are not used for the physical total.

## Fans on the motherboard

Super I/O, embedded-controller, cooler, and motherboard fan rows are listed under `ambiguousFans` with possible roles `fan.cpu` and `fan.case`. A header named `CPU Fan` is still ambiguous. Board vendors reuse that label, and a splitter can put both Fuma 2 fans on one tach. Choose the ids after you have looked at the inventory, then put them in `fanCpu` or `fanCase`.

0 RPM with status `ok` is a stop. Status `missing`, `stale`, `unsupported`, or `error` is not a stop, and the value is null.

## What "read-only" does not promise

Opening the Super I/O while Fan Control is also polling it can still disturb a curve. That is why the interval is 1 second and why motherboard polling can be turned off. CPU MSR and SMN reads go through the same PawnIO device Fan Control uses. They are not free either.

No measurement of this process's CPU time, RAM, or of a game's frametime was taken. Those have to be measured on the Windows PC, with Fan Control running, before anyone calls the overhead small.

## Synthetic mode

`--synthetic` never loads LibreHardwareMonitor. Ids start with `/synthetic/`. `synthetic` is true on the document and on the source. The fixture includes two CPU temperatures, sixteen named thread-load rows, a GPU fan at 0 RPM, a motherboard tach with a null value, and a stale temperature. It is shaped like the described PC so the JSON has somewhere to put those cases. It is not a recording of that PC.
