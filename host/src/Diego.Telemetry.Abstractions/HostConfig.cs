namespace Diego.Telemetry;

public sealed class HostConfig
{
    public double PollIntervalSeconds { get; set; } = 1;

    public bool MotherboardPolling { get; set; } = true;

    public bool AcceptUniqueCandidates { get; set; }

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
