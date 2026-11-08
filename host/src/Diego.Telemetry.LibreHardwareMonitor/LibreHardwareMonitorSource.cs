using System.Reflection;
using LibreHardwareMonitor.Hardware;

namespace Diego.Telemetry.LibreHardwareMonitor;

/// <summary>
/// Read-only adapter over LibreHardwareMonitorLib 0.9.6. It never writes
/// <see cref="IControl"/>, never assigns sensor names, and never launches PawnIO setup.
/// </summary>
public sealed class LibreHardwareMonitorSource : IHardwareSource
{
    public const string PinnedPackageVersion = "0.9.6";

    private readonly HostConfig _config;
    private readonly bool _skipWarmup;
    private readonly List<string> _warnings = [];
    private readonly PawnIoReport _pawn;
    private Computer? _computer;
    private bool _opened;
    private bool _warmed;
    private bool _disposed;
    private Dictionary<string, string> _groups = new(StringComparer.Ordinal);

    public LibreHardwareMonitorSource(HostConfig config, bool skipWarmup)
    {
        _config = config;
        _skipWarmup = skipWarmup;
        _pawn = PawnIoProbe.Read();
    }

    public string Kind => "librehardwaremonitor";

    public bool IsSynthetic => false;

    public async Task<HardwareObservation> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureOpen();
        var suppressCpuLoad = false;
        if (!_warmed)
        {
            UpdateAll();
            if (!_skipWarmup)
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
                UpdateAll();
            }
            else
            {
                suppressCpuLoad = true;
                _warnings.Add(
                    "skip-warmup was set. LibreHardwareMonitor's first CPU load update can publish 0 percent because CpuLoad.Update returns early on a short window while GenericCpu still assigns GetThreadLoad. CPU load sensors from this sample are reported as errors with a null value.");
            }

            _warmed = true;
        }
        else
        {
            UpdateAll();
        }

        return Collect(suppressCpuLoad);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (_computer is not null)
        {
            try
            {
                _computer.Close();
            }
            catch (Exception ex)
            {
                _warnings.Add($"Computer.Close failed: {ex.GetType().Name}: {ex.Message}");
            }

            _computer = null;
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (_opened)
        {
            return;
        }

        var pawnInstalled = _pawn.Installed == true;
        var openMotherboard = _config.MotherboardPolling && pawnInstalled;
        if (_config.MotherboardPolling && !pawnInstalled)
        {
            _warnings.Add(
                "Motherboard sensors were not opened because PawnIO is not installed. CPU-cooler and case-fan tachometers usually live on the Super I/O. Install the signed driver yourself from https://pawnio.eu/ or https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0. This program does not install it and does not fall back to WinRing0.");
        }
        else if (!_config.MotherboardPolling)
        {
            _warnings.Add(
                "motherboardPolling is false. Super I/O and embedded-controller sensors are not opened, so this process does not share that poll with Fan Control. CPU and GPU groups are unchanged.");
        }
        else
        {
            _warnings.Add(
                "Motherboard polling is on. LibreHardwareMonitor and Fan Control can contend on the same super I/O even though this host only reads. The interval is the configured poll (default 1 second). Fan duty and Fan Control's configuration are not changed. Set motherboardPolling to false if curves glitch.");
        }

        if (!pawnInstalled)
        {
            _warnings.Add(
                "PawnIO is not installed. CPU temperature in LibreHardwareMonitorLib 0.9.6 uses the PawnIO driver. Those temperatures are reported as errors with a null value so a zeroed driver buffer is not shown as 0 C. CPU load still uses NtQuerySystemInformation and does not need PawnIO. GPU sensors use NVAPI. Memory uses GlobalMemoryStatusEx.");
        }

        _groups = new Dictionary<string, string>
        {
            ["cpu"] = "opened",
            ["gpu"] = "opened",
            ["memory"] = "opened",
            ["motherboard"] = openMotherboard ? "opened" : pawnInstalled ? "disabled-by-config" : "not-opened-pawnio-missing",
            ["controller"] = "disabled",
            ["storage"] = "disabled",
            ["network"] = "disabled",
            ["psu"] = "disabled",
            ["battery"] = "disabled",
            ["powerMonitor"] = "disabled"
        };

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = openMotherboard,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsPsuEnabled = false,
            IsBatteryEnabled = false,
            IsPowerMonitorEnabled = false
        };

        try
        {
            computer.Open();
        }
        catch (Exception ex)
        {
            try
            {
                computer.Close();
            }
            catch (Exception closeEx)
            {
                _warnings.Add($"Computer.Close after a failed Open failed: {closeEx.GetType().Name}: {closeEx.Message}");
            }

            throw new HardwareAccessException($"LibreHardwareMonitor Computer.Open failed: {ex.GetType().Name}: {ex.Message}");
        }

        _computer = computer;
        _opened = true;
    }

    private void UpdateAll()
    {
        if (_computer is null)
        {
            return;
        }

        foreach (var hardware in _computer.Hardware)
        {
            UpdateTree(hardware);
        }
    }

    private void UpdateTree(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            _warnings.Add($"Update failed for {hardware.Identifier}: {ex.GetType().Name}: {ex.Message}");
        }

        foreach (var sub in hardware.SubHardware)
        {
            UpdateTree(sub);
        }
    }

    private HardwareObservation Collect(bool suppressCpuLoad)
    {
        var sensors = new List<SensorSample>();
        if (_computer is not null)
        {
            foreach (var hardware in _computer.Hardware)
            {
                CollectTree(hardware, sensors, suppressCpuLoad);
            }
        }

        var assemblyVersion = typeof(Computer).Assembly.GetName().Version?.ToString();
        return new HardwareObservation
        {
            Kind = Kind,
            Synthetic = false,
            PackageId = "LibreHardwareMonitorLib",
            PackageVersion = PinnedPackageVersion,
            AssemblyVersion = assemblyVersion,
            Backend = new BackendReport
            {
                PawnIo = _pawn,
                Groups = _groups,
                Warnings = _warnings.ToArray()
            },
            Sensors = sensors
        };
    }

    private void CollectTree(IHardware hardware, List<SensorSample> sensors, bool suppressCpuLoad)
    {
        foreach (var sensor in hardware.Sensors)
        {
            sensors.Add(ToSample(hardware, sensor, suppressCpuLoad));
        }

        foreach (var sub in hardware.SubHardware)
        {
            CollectTree(sub, sensors, suppressCpuLoad);
        }
    }

    private SensorSample ToSample(IHardware hardware, ISensor sensor, bool suppressCpuLoad)
    {
        var id = sensor.Identifier.ToString() ?? "";
        var path = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        float? raw = sensor.Value;
        double? libraryValue = raw.HasValue ? raw.Value : null;
        var state = SampleState.Fresh;
        ReadingError? error = null;
        if (libraryValue is null)
        {
            state = SampleState.Missing;
            error = new ReadingError("library_null", "LibreHardwareMonitor left this sensor value null. It is not reported as 0.");
        }
        else if (!double.IsFinite(libraryValue.Value))
        {
            state = SampleState.Error;
            libraryValue = null;
            error = new ReadingError("non_finite", "LibreHardwareMonitor returned a non-finite sensor value.");
        }

        var hardwareType = hardware.HardwareType.ToString();
        var sensorType = sensor.SensorType.ToString();
        var pawnInstalled = _pawn.Installed == true;
        if (hardwareType == "Cpu" && sensorType == "Temperature")
        {
            if (!pawnInstalled)
            {
                state = SampleState.Error;
                error = new ReadingError(
                    "pawnio_not_installed",
                    "CPU temperature was not trusted. LibreHardwareMonitorLib 0.9.6 reads AMD family 17h/19h temperatures through PawnIO. PawnIo.Execute returns a zero-filled buffer when the device is not open, and Amd17Cpu scales that buffer to 0 C. value is null. Install the signed PawnIO driver yourself. This program does not install it and does not use WinRing0.");
            }
            else if (libraryValue == 0d)
            {
                state = SampleState.Error;
                error = new ReadingError(
                    "zeroed_driver_buffer",
                    "LibreHardwareMonitor reported 0 C for this CPU temperature. A failed PawnIO module read becomes 0 C for the same reason: PawnIo.Execute returns a zero-filled buffer. value is null. libraryValue keeps the 0 so the raw report is visible. A genuine 0 C reading would also be hidden by this guard.");
            }
        }

        if (suppressCpuLoad && hardwareType == "Cpu" && sensorType == "Load" && state == SampleState.Fresh)
        {
            state = SampleState.Error;
            error = new ReadingError(
                "warmup_skipped",
                "CPU load was not published because skip-warmup was set. The library can store 0 percent on the first short window. value is null.");
        }

        var converted = SensorUnits.Convert(sensorType, libraryValue);
        double? normalized = state == SampleState.Fresh ? converted.NormalizedValue : null;
        if (sensorType is not ("Temperature" or "Load" or "Fan" or "Data" or "SmallData" or "Control" or "Level" or "Clock" or "Voltage" or "Current" or "Power" or "Frequency" or "Flow" or "Throughput" or "Factor" or "TimeSpan" or "Timing" or "Energy" or "Noise" or "Conductivity" or "Humidity"))
        {
            if (state == SampleState.Fresh)
            {
                state = SampleState.Unsupported;
                normalized = null;
                error = new ReadingError("unknown_sensor_type", $"Sensor type {sensorType} has no unit mapping in this host.");
            }
        }

        return new SensorSample
        {
            Id = id,
            Path = path,
            HardwareId = hardware.Identifier.ToString() ?? "",
            ParentHardwareId = hardware.Parent?.Identifier.ToString(),
            HardwareName = hardware.Name,
            HardwareType = hardwareType,
            Name = sensor.Name,
            SensorType = sensorType,
            Index = sensor.Index,
            LibraryValue = libraryValue,
            LibraryUnit = converted.LibraryUnit,
            NormalizedValue = normalized,
            NormalizedUnit = converted.NormalizedUnit,
            State = state,
            Error = error,
            ObservedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
