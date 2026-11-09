// Operator role map. Null means unmapped, which stays null in the JSON.
namespace Diego.Telemetry;

public sealed class HostConfig
{
    public string ConfigSchema { get; set; } = "0.2";

    public double PollIntervalSeconds { get; set; } = 1;

    public bool MotherboardPolling { get; set; } = true;

    public bool AcceptUniqueCandidates { get; set; }

    /// <summary>
    /// When true, unambiguous CPU, GPU, RAM, VRAM, and GPU-fan roles are filled
    /// from sensor names. Motherboard fan roles stay empty. Default for a
    /// hand-built test config is false so older tests keep explicit ids.
    /// The installed example turns it on.
    /// </summary>
    public bool AutoMap { get; set; }

    public int PublishPort { get; set; } = 8765;

    public int DiscoveryPort { get; set; } = 8764;

    public bool DiscoveryEnabled { get; set; } = true;

    /// <summary>Logon task. Default on. The installer skips it when the off marker exists.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// Ask the paired Pi to halt when Windows shuts down or restarts.
    /// Tray Exit does not use this. Default on. The checkbox turns it off.
    /// </summary>
    public bool PowerOffPiOnShutdown { get; set; } = true;

    /// <summary>The first hide-to-tray hint has already been shown.</summary>
    public bool TrayHintShown { get; set; }

    public double StaleAfterSeconds { get; set; } = 5;

    public RoleMap Roles { get; set; } = new();
}

public sealed class RoleMap
{
    public string? CpuTemperature { get; set; }

    public string? GpuTemperature { get; set; }

    public string? CpuUtilizationTotal { get; set; }

    public List<string> CpuUtilizationLogical { get; set; } = [];

    public string? GpuLoad { get; set; }

    public string? MemoryUsed { get; set; }

    public string? MemoryTotal { get; set; }

    public string? VramUsed { get; set; }

    public string? VramTotal { get; set; }

    public List<string> FanCpu { get; set; } = [];

    public List<string> FanGpu { get; set; } = [];

    public List<string> FanCase { get; set; } = [];
}
