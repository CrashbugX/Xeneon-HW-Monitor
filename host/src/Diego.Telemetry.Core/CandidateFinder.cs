// Lists possible role bindings. verified is always false. Header names are not treated as proof.
namespace Diego.Telemetry;

public static class CandidateFinder
{
    private static readonly string[] BoardFanTypes = ["Motherboard", "SuperIO", "EmbeddedController", "Cooler"];

    private static readonly string[] GpuTypes = ["GpuNvidia", "GpuAmd", "GpuIntel"];

    public static (IReadOnlyList<SensorCandidate> Candidates, IReadOnlyList<AmbiguousFan> AmbiguousFans) Find(
        IReadOnlyList<SensorSample> sensors)
    {
        var candidates = new List<SensorCandidate>();
        Add(candidates, sensors, "cpu.temperature", static s => s.HardwareType == "Cpu" && s.SensorType == "Temperature",
            "Temperature sensor on a CPU hardware node. Ryzen parts can expose Tctl, Tdie, Tctl/Tdie, and CCD sensors together. This row does not select one of them.");
        Add(candidates, sensors, "gpu.temperature", static s => IsGpu(s) && s.SensorType == "Temperature",
            "Temperature sensor on a GPU hardware node. Core, hotspot, and memory-junction channels are separate. This row does not choose one.");
        Add(candidates, sensors, "cpu.utilization.total", static s => s.HardwareType == "Cpu" && s.SensorType == "Load" && s.Name == "CPU Total",
            "The library name is exactly \"CPU Total\". That name is not a verified binding.");
        Add(candidates, sensors, "cpu.utilization.logical", IsLogicalLoad,
            "LibreHardwareMonitor names this load sensor from CPUID core and thread numbers. That label is not a verified OS logical-processor index. A 16-thread CPU is not guaranteed to expose 16 load sensors.");
        Add(candidates, sensors, "gpu.load", static s => IsGpu(s) && s.SensorType == "Load",
            "Load sensor on a GPU hardware node. Names such as GPU Core, memory, or a D3D engine are not treated as the full GPU load.");
        Add(candidates, sensors, "memory.used", static s => PhysicalMemory.IsPhysicalData(s, "Memory Used"),
            "Library name is exactly \"Memory Used\" on the physical RAM node. Virtual Memory uses the same name and is not a candidate. verified stays false.");
        Add(candidates, sensors, "memory.total", static s => s.HardwareType == "Memory" && s.SensorType == "Data" && s.Name == "Memory Total",
            "A sensor literally named \"Memory Total\". LibreHardwareMonitorLib 0.9.6 does not create one; physical total is derived from Used plus Available and is not given a sensor id.");
        Add(candidates, sensors, "vram.used", IsVramUsed,
            "SmallData sensor whose library name is \"GPU Memory Used\" or \"D3D Dedicated Memory Used\". Those are different counters. Neither name is treated as proof of dedicated VRAM.");
        Add(candidates, sensors, "vram.total", static s => IsGpu(s) && s.SensorType == "SmallData" && s.Name == "GPU Memory Total",
            "Library name is exactly \"GPU Memory Total\". This is not treated as proof of dedicated VRAM capacity.");
        Add(candidates, sensors, "fan.gpu", static s => IsGpu(s) && s.SensorType == "Fan",
            "Fan tach on a GPU hardware node. A later 0 RPM with status ok means the tach reported stop. verified is false.");

        var ambiguous = sensors
            .Where(s => s.SensorType == "Fan" && BoardFanTypes.Contains(s.HardwareType, StringComparer.Ordinal))
            .Where(s => !s.Name.Contains("chipset", StringComparison.OrdinalIgnoreCase))
            .Select(s => new AmbiguousFan
            {
                SensorId = s.Id,
                HardwareType = s.HardwareType,
                SourceName = s.Name,
                PossibleRoles = ["fan.cpu", "fan.case"],
                Verified = false,
                Reason = "Fan tach on motherboard, Super I/O, embedded-controller, or cooler hardware. Header names such as CPU Fan or SYS_FAN1 are not used to decide CPU cooler versus case fan. A splitter can make both Scythe Fuma 2 fans appear as one tach."
            })
            .ToArray();

        return (candidates, ambiguous);
    }

    private static void Add(
        List<SensorCandidate> destination,
        IReadOnlyList<SensorSample> sensors,
        string role,
        Func<SensorSample, bool> match,
        string reason)
    {
        var matched = sensors.Where(match).ToArray();
        var unique = matched.Length == 1;
        foreach (var sensor in matched)
        {
            destination.Add(new SensorCandidate
            {
                Role = role,
                SensorId = sensor.Id,
                HardwareType = sensor.HardwareType,
                SensorType = sensor.SensorType,
                SourceName = sensor.Name,
                Unique = unique,
                Verified = false,
                Reason = reason
            });
        }
    }

    private static bool IsGpu(SensorSample sample)
    {
        return GpuTypes.Contains(sample.HardwareType, StringComparer.Ordinal);
    }

    private static bool IsLogicalLoad(SensorSample sample)
    {
        return sample.HardwareType == "Cpu"
            && sample.SensorType == "Load"
            && sample.Name != "CPU Total"
            && sample.Name != "CPU Core Max"
            && sample.Name.StartsWith("CPU Core", StringComparison.Ordinal);
    }

    private static bool IsVramUsed(SensorSample sample)
    {
        return IsGpu(sample)
            && sample.SensorType == "SmallData"
            && (sample.Name == "GPU Memory Used" || sample.Name == "D3D Dedicated Memory Used");
    }
}
