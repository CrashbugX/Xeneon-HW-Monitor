namespace Diego.Telemetry.Fake;

/// <summary>
/// Fixed fixture shaped like the described PC. Every id is under /synthetic and the
/// observation is marked synthetic. Nothing here was read from hardware.
/// </summary>
public sealed class SyntheticHardwareSource : IHardwareSource
{
    private readonly DateTimeOffset _observedAtUtc;
    private bool _disposed;

    public SyntheticHardwareSource(DateTimeOffset? observedAtUtc = null)
    {
        _observedAtUtc = observedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public string Kind => "synthetic";

    public bool IsSynthetic => true;

    public Task<HardwareObservation> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult(Build(_observedAtUtc));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    public static HardwareObservation Build(DateTimeOffset observedAtUtc)
    {
        var sensors = new List<SensorSample>
        {
            Sample("cpu", "0", "Cpu", "SYNTHETIC Ryzen 7 5800X3D", "temperature", 0, "Core (Tctl/Tdie)", "Temperature", 61.25, SampleState.Fresh, observedAtUtc),
            Sample("cpu", "0", "Cpu", "SYNTHETIC Ryzen 7 5800X3D", "temperature", 1, "CCD1 (Tdie)", "Temperature", 58.5, SampleState.Fresh, observedAtUtc),
            Sample("cpu", "0", "Cpu", "SYNTHETIC Ryzen 7 5800X3D", "temperature", 2, "Stale diode", "Temperature", 70, SampleState.Stale, observedAtUtc, new ReadingError("stale_fixture", "Fixture sensor marked stale. Its value must be published as null.")),
            Sample("cpu", "0", "Cpu", "SYNTHETIC Ryzen 7 5800X3D", "load", 0, "CPU Total", "Load", 22.5, SampleState.Fresh, observedAtUtc),
            Sample("cpu", "0", "Cpu", "SYNTHETIC Ryzen 7 5800X3D", "load", 1, "CPU Core Max", "Load", 40, SampleState.Fresh, observedAtUtc)
        };

        for (var core = 1; core <= 8; core++)
        {
            for (var thread = 1; thread <= 2; thread++)
            {
                var index = 2 + ((core - 1) * 2) + (thread - 1);
                sensors.Add(Sample(
                    "cpu",
                    "0",
                    "Cpu",
                    "SYNTHETIC Ryzen 7 5800X3D",
                    "load",
                    index,
                    $"CPU Core #{core} Thread #{thread}",
                    "Load",
                    10 + thread,
                    SampleState.Fresh,
                    observedAtUtc));
            }
        }

        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "temperature", 0, "GPU Core", "Temperature", 64, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "temperature", 1, "GPU Hot Spot", "Temperature", 76.5, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "load", 0, "GPU Core", "Load", 48, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "load", 1, "GPU Memory", "Load", 30, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "smalldata", 0, "GPU Memory Free", "SmallData", 4692, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "smalldata", 1, "GPU Memory Used", "SmallData", 3500, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "smalldata", 2, "GPU Memory Total", "SmallData", 8192, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "smalldata", 3, "D3D Dedicated Memory Used", "SmallData", 3400, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("gpu", "0", "GpuNvidia", "SYNTHETIC RTX 2080 SUPER", "fan", 0, "GPU", "Fan", 0, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("ram", null, "Memory", "SYNTHETIC 32 GiB", "data", 0, "Memory Used", "Data", 12.5, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("ram", null, "Memory", "SYNTHETIC 32 GiB", "data", 1, "Memory Available", "Data", 19.5, SampleState.Fresh, observedAtUtc));
        sensors.Add(Sample("superio", "0", "SuperIO", "SYNTHETIC motherboard", "fan", 0, "CPU Fan", "Fan", 900, SampleState.Fresh, observedAtUtc, parent: "/synthetic/motherboard/0"));
        sensors.Add(Sample("superio", "0", "SuperIO", "SYNTHETIC motherboard", "fan", 1, "Fan #2", "Fan", 0, SampleState.Fresh, observedAtUtc, parent: "/synthetic/motherboard/0"));
        sensors.Add(Sample("superio", "0", "SuperIO", "SYNTHETIC motherboard", "fan", 2, "SYS_FAN1", "Fan", null, SampleState.Missing, observedAtUtc, new ReadingError("missing_fixture", "Fixture tach with no reading. Value must stay null, not 0."), parent: "/synthetic/motherboard/0"));

        return new HardwareObservation
        {
            Kind = "synthetic",
            Synthetic = true,
            PackageId = null,
            PackageVersion = null,
            AssemblyVersion = null,
            Backend = new BackendReport
            {
                PawnIo = new PawnIoReport
                {
                    Checked = false,
                    Installed = null,
                    DisplayVersion = null,
                    Detection = "Not applicable. Synthetic mode does not look at the PawnIO registry key or device."
                },
                Groups = new Dictionary<string, string>
                {
                    ["cpu"] = "synthetic",
                    ["gpu"] = "synthetic",
                    ["memory"] = "synthetic",
                    ["motherboard"] = "synthetic",
                    ["controller"] = "disabled",
                    ["storage"] = "disabled",
                    ["network"] = "disabled"
                },
                Warnings =
                [
                    "Synthetic fixture only. It is shaped like a Ryzen 7 5800X3D, RTX 2080 SUPER, 32 GiB of RAM, and a dual-fan cooler, and it was not read from that PC."
                ]
            },
            Sensors = sensors
        };
    }

    private static SensorSample Sample(
        string hardware,
        string? hardwareIndex,
        string hardwareType,
        string hardwareName,
        string channel,
        int index,
        string name,
        string sensorType,
        double? libraryValue,
        SampleState state,
        DateTimeOffset observedAtUtc,
        ReadingError? error = null,
        string? parent = null)
    {
        var hardwareId = hardwareIndex is null
            ? $"/synthetic/{hardware}"
            : $"/synthetic/{hardware}/{hardwareIndex}";
        var id = $"{hardwareId}/{channel}/{index}";
        var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var converted = SensorUnits.Convert(sensorType, libraryValue);
        var normalized = state == SampleState.Fresh ? converted.NormalizedValue : null;
        return new SensorSample
        {
            Id = id,
            Path = parts,
            HardwareId = hardwareId,
            ParentHardwareId = parent,
            HardwareName = hardwareName,
            HardwareType = hardwareType,
            Name = name,
            SensorType = sensorType,
            Index = index,
            LibraryValue = libraryValue,
            LibraryUnit = converted.LibraryUnit,
            NormalizedValue = normalized,
            NormalizedUnit = converted.NormalizedUnit,
            State = state,
            Error = error,
            ObservedAtUtc = observedAtUtc
        };
    }
}
