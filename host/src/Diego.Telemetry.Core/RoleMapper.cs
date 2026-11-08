// Semantic role selection. Configured ids win. Ambiguous fans are never guessed.
using System.Text.RegularExpressions;

namespace Diego.Telemetry;

/// <summary>
/// Chooses dashboard roles from hardware id, sensor type, unit, and the
/// library's source name. A configured id always wins. With <c>autoMap</c>,
/// a role is filled only when one sensor has the expected meaning.
/// Motherboard fan headers are not assigned to the CPU cooler or the case.
/// <c>verified</c> stays false: a name is not a physical inspection.
/// </summary>
public static class RoleMapper
{
    private static readonly string[] GpuTypes = ["GpuNvidia", "GpuAmd", "GpuIntel"];

    private static readonly Regex LogicalLoad = new(
        @"^CPU Core #(?<core>\d+)(?: Thread #(?<thread>\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static RolePlan Plan(IReadOnlyList<SensorSample> sensors, HostConfig config)
    {
        var cpuTemp = One(sensors, static s => s.HardwareType == "Cpu" && s.SensorType == "Temperature" && s.Name == "Core (Tctl/Tdie)");
        var cpuTotal = One(sensors, static s => s.HardwareType == "Cpu" && s.SensorType == "Load" && s.Name == "CPU Total");
        var logical = LogicalLoads(sensors);
        var gpuTemp = One(sensors, static s => IsGpu(s) && s.SensorType == "Temperature" && s.Name == "GPU Core");
        var gpuLoad = One(sensors, static s => IsGpu(s) && s.SensorType == "Load" && s.Name == "GPU Core");
        var memoryUsed = One(sensors, static s => PhysicalMemory.IsPhysicalData(s, "Memory Used"));
        var vramUsed = One(sensors, static s => IsGpu(s) && s.SensorType == "SmallData" && s.Name == "GPU Memory Used");
        var vramTotal = One(sensors, static s => IsGpu(s) && s.SensorType == "SmallData" && s.Name == "GPU Memory Total");
        var gpuFans = sensors.Where(static s => IsGpu(s) && s.SensorType == "Fan").Select(static s => s.Id).ToArray();

        var decisions = new List<MappingDecision>
        {
            Decide("cpu.temperature", config.Roles.CpuTemperature, cpuTemp, config.AutoMap,
                "Core (Tctl/Tdie) on the CPU node. CCD and embedded-controller temperatures are different sensors and are not substituted."),
            Decide("cpu.utilization.total", config.Roles.CpuUtilizationTotal, cpuTotal, config.AutoMap,
                "The load sensor named exactly CPU Total."),
            DecideList("cpu.utilization.logical", config.Roles.CpuUtilizationLogical, logical.Ids, logical.Ambiguous, config.AutoMap,
                "Load sensors named CPU Core #N or CPU Core #N Thread #M, ordered by those numbers. CPU Total and CPU Core Max are not in this list. The names are not a verified OS topology."),
            Decide("gpu.temperature", config.Roles.GpuTemperature, gpuTemp, config.AutoMap,
                "The GPU temperature named GPU Core. Hot Spot and Memory Junction stay off this role."),
            Decide("gpu.load", config.Roles.GpuLoad, gpuLoad, config.AutoMap,
                "The GPU load named GPU Core. GPU Memory, GPU Bus, and D3D nodes are different percentages and are not this role."),
            Decide("memory.used", config.Roles.MemoryUsed, memoryUsed, config.AutoMap,
                "Memory Used on the physical RAM node. The virtual-memory node uses the same library name and is not physical RAM."),
            Decide("vram.used", config.Roles.VramUsed, vramUsed, config.AutoMap,
                "SmallData named GPU Memory Used, converted from binary mebibytes. D3D Dedicated Memory Used is a different counter and is not substituted. Used plus free is not forced equal to total."),
            Decide("vram.total", config.Roles.VramTotal, vramTotal, config.AutoMap,
                "SmallData named GPU Memory Total. The library value is kept even when used plus free is short of it."),
            DecideList("fan.gpu", config.Roles.FanGpu, gpuFans, ambiguous: false, config.AutoMap,
                "Fan tachometers on the GPU hardware node. Control-percent rows are not RPM."),
            DecideList("fan.cpu", config.Roles.FanCpu, [], ambiguous: true, config.AutoMap,
                "No motherboard tach is assigned to the CPU cooler. Header index, RPM, and a splitter are not evidence."),
            DecideList("fan.case", config.Roles.FanCase, [], ambiguous: true, config.AutoMap,
                "No motherboard tach is assigned to a case fan. Choose the ids in the host window after you know the headers.")
        };

        return new RolePlan
        {
            CpuTemperature = Choose(config.Roles.CpuTemperature, cpuTemp, config.AutoMap),
            CpuUtilizationTotal = Choose(config.Roles.CpuUtilizationTotal, cpuTotal, config.AutoMap),
            CpuUtilizationLogical = ChooseList(config.Roles.CpuUtilizationLogical, logical.Ids, logical.Ambiguous, config.AutoMap),
            GpuTemperature = Choose(config.Roles.GpuTemperature, gpuTemp, config.AutoMap),
            GpuLoad = Choose(config.Roles.GpuLoad, gpuLoad, config.AutoMap),
            MemoryUsed = Choose(config.Roles.MemoryUsed, memoryUsed, config.AutoMap),
            VramUsed = Choose(config.Roles.VramUsed, vramUsed, config.AutoMap),
            VramTotal = Choose(config.Roles.VramTotal, vramTotal, config.AutoMap),
            FanGpu = ChooseList(config.Roles.FanGpu, gpuFans, ambiguous: false, config.AutoMap),
            FanCpu = config.Roles.FanCpu.Count > 0 ? config.Roles.FanCpu : [],
            FanCase = config.Roles.FanCase.Count > 0 ? config.Roles.FanCase : [],
            Decisions = decisions,
            FanChoices = ClassifyFans(sensors, gpuFans)
        };
    }

    private static IReadOnlyList<FanChoice> ClassifyFans(IReadOnlyList<SensorSample> sensors, IReadOnlyList<string> gpuFanIds)
    {
        var gpu = new HashSet<string>(gpuFanIds, StringComparer.Ordinal);
        var choices = new List<FanChoice>();
        foreach (var sensor in sensors.Where(static s => s.SensorType == "Fan"))
        {
            var chipset = sensor.Name.Contains("chipset", StringComparison.OrdinalIgnoreCase);
            string classification;
            IReadOnlyList<string> roles;
            string reason;
            if (gpu.Contains(sensor.Id))
            {
                classification = "gpu";
                roles = ["fan.gpu"];
                reason = "Fan tach on the GPU node. A 0 RPM reading with status ok is a reported stop, not a missing header.";
            }
            else if (chipset)
            {
                classification = "chipset";
                roles = [];
                reason = "The source name says chipset. It is not offered as a CPU-cooler or case-fan choice.";
            }
            else
            {
                classification = "needs-user";
                roles = ["fan.cpu", "fan.case"];
                reason = "Board or embedded-controller tach with no verified header. Index, speed, and a zero reading do not decide CPU cooler versus case. verified is false.";
            }

            choices.Add(new FanChoice
            {
                SensorId = sensor.Id,
                HardwareType = sensor.HardwareType,
                HardwareName = sensor.HardwareName,
                SourceName = sensor.Name,
                Classification = classification,
                PossibleRoles = roles,
                Verified = false,
                Reason = reason
            });
        }

        return choices;
    }

    private static (IReadOnlyList<string> Ids, bool Ambiguous) LogicalLoads(IReadOnlyList<SensorSample> sensors)
    {
        var matches = new List<(int Core, int Thread, string Id)>();
        foreach (var sensor in sensors)
        {
            if (sensor.HardwareType != "Cpu" || sensor.SensorType != "Load")
            {
                continue;
            }

            var match = LogicalLoad.Match(sensor.Name);
            if (!match.Success)
            {
                continue;
            }

            var core = int.Parse(match.Groups["core"].Value);
            var thread = match.Groups["thread"].Success ? int.Parse(match.Groups["thread"].Value) : 0;
            matches.Add((core, thread, sensor.Id));
        }

        var duplicate = matches.GroupBy(item => (item.Core, item.Thread)).Any(group => group.Count() > 1);
        var ids = matches.OrderBy(item => item.Core).ThenBy(item => item.Thread).Select(item => item.Id).ToArray();
        return (ids, duplicate || ids.Length == 0);
    }

    private static string? One(IReadOnlyList<SensorSample> sensors, Func<SensorSample, bool> match)
    {
        var found = sensors.Where(match).Select(static sensor => sensor.Id).Distinct(StringComparer.Ordinal).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    private static Chosen Choose(string? configured, string? semantic, bool autoMap)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Chosen(configured, "configured");
        }

        if (autoMap && semantic is not null)
        {
            return new Chosen(semantic, "semantic");
        }

        return new Chosen(null, "none");
    }

    private static ChosenList ChooseList(IReadOnlyList<string> configured, IReadOnlyList<string> semantic, bool ambiguous, bool autoMap)
    {
        if (configured.Count > 0)
        {
            return new ChosenList(configured, "configured");
        }

        if (autoMap && !ambiguous && semantic.Count > 0)
        {
            return new ChosenList(semantic, "semantic");
        }

        return new ChosenList([], "none");
    }

    private static MappingDecision Decide(string role, string? configured, string? semantic, bool autoMap, string reason)
    {
        var chosen = Choose(configured, semantic, autoMap);
        var outcome = chosen.Binding == "none" ? (semantic is null && string.IsNullOrWhiteSpace(configured) ? "ambiguous" : "unmapped") : chosen.Binding;
        if (string.IsNullOrWhiteSpace(configured) && semantic is null)
        {
            outcome = "ambiguous";
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            outcome = "configured";
        }
        else if (autoMap && semantic is not null)
        {
            outcome = "semantic";
        }
        else if (!autoMap)
        {
            outcome = "unmapped";
        }

        return new MappingDecision
        {
            Role = role,
            Outcome = outcome,
            SensorId = chosen.Id,
            Reason = reason,
            Verified = false
        };
    }

    private static MappingDecision DecideList(
        string role,
        IReadOnlyList<string> configured,
        IReadOnlyList<string> semantic,
        bool ambiguous,
        bool autoMap,
        string reason)
    {
        var chosen = ChooseList(configured, semantic, ambiguous, autoMap);
        var outcome = chosen.Binding == "configured"
            ? "configured"
            : chosen.Binding == "semantic"
                ? "semantic"
                : autoMap ? "ambiguous" : "unmapped";
        return new MappingDecision
        {
            Role = role,
            Outcome = outcome,
            SensorId = chosen.Ids.Count == 0 ? null : string.Join(",", chosen.Ids),
            Reason = reason,
            Verified = false
        };
    }

    private static bool IsGpu(SensorSample sample)
    {
        return GpuTypes.Contains(sample.HardwareType, StringComparer.Ordinal);
    }
}

public sealed class RolePlan
{
    public Chosen CpuTemperature { get; init; } = new(null, "none");

    public Chosen CpuUtilizationTotal { get; init; } = new(null, "none");

    public ChosenList CpuUtilizationLogical { get; init; } = new([], "none");

    public Chosen GpuTemperature { get; init; } = new(null, "none");

    public Chosen GpuLoad { get; init; } = new(null, "none");

    public Chosen MemoryUsed { get; init; } = new(null, "none");

    public Chosen VramUsed { get; init; } = new(null, "none");

    public Chosen VramTotal { get; init; } = new(null, "none");

    public ChosenList FanGpu { get; init; } = new([], "none");

    public IReadOnlyList<string> FanCpu { get; init; } = [];

    public IReadOnlyList<string> FanCase { get; init; } = [];

    public IReadOnlyList<MappingDecision> Decisions { get; init; } = [];

    public IReadOnlyList<FanChoice> FanChoices { get; init; } = [];
}

public sealed record Chosen(string? Id, string Binding);

public sealed record ChosenList(IReadOnlyList<string> Ids, string Binding);
