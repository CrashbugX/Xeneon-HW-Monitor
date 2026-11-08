using System.Text.Json.Serialization;

namespace Diego.Telemetry;

public sealed class TelemetryDocument
{
    public string SchemaVersion { get; init; } = TelemetrySchema.Version;

    public bool SampledOnce { get; init; }

    public bool Synthetic { get; init; }

    public int? Sequence { get; init; }

    public DateTimeOffset SampledAtUtc { get; init; }

    public SourceInfo Source { get; init; } = new();

    public PollInfo Poll { get; init; } = new();

    public BackendReport Backend { get; init; } = new()
    {
        PawnIo = new PawnIoReport { Checked = false, Detection = "" },
        Groups = new Dictionary<string, string>(),
        Warnings = []
    };

    public IReadOnlyList<string> Limitations { get; init; } = LimitationText.Always;

    public IReadOnlyList<InventorySensor> Inventory { get; init; } = [];

    public IReadOnlyList<SensorCandidate> Candidates { get; init; } = [];

    public IReadOnlyList<AmbiguousFan> AmbiguousFans { get; init; } = [];

    public ReadingSet Readings { get; init; } = new();

    public DerivedMemory Derived { get; init; } = new();
}

public sealed class SourceInfo
{
    public string Kind { get; init; } = "";

    public bool Synthetic { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    public string? AssemblyVersion { get; init; }
}

public sealed class PollInfo
{
    public double IntervalSeconds { get; init; }

    public double MinimumIntervalSeconds { get; init; }

    public double StaleAfterSeconds { get; init; }

    public bool MotherboardPolling { get; init; }

    public bool AcceptUniqueCandidates { get; init; }
}

public sealed class InventorySensor
{
    public required string Id { get; init; }

    public required IReadOnlyList<string> Path { get; init; }

    public required string HardwareId { get; init; }

    public string? ParentHardwareId { get; init; }

    public required string HardwareName { get; init; }

    public required string HardwareType { get; init; }

    public required string Name { get; init; }

    public required string SensorType { get; init; }

    public required int Index { get; init; }

    public required string Status { get; init; }

    public double? LibraryValue { get; init; }

    public required string LibraryUnit { get; init; }

    public double? NormalizedValue { get; init; }

    public required string NormalizedUnit { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public ReadingError? Error { get; init; }
}

public sealed class SensorCandidate
{
    public required string Role { get; init; }

    public required string SensorId { get; init; }

    public required string HardwareType { get; init; }

    public required string SensorType { get; init; }

    public required string SourceName { get; init; }

    public required bool Unique { get; init; }

    public bool Verified { get; init; }

    public required string Reason { get; init; }
}

public sealed class AmbiguousFan
{
    public required string SensorId { get; init; }

    public required string HardwareType { get; init; }

    public required string SourceName { get; init; }

    public required IReadOnlyList<string> PossibleRoles { get; init; }

    public bool Verified { get; init; }

    public required string Reason { get; init; }
}

public sealed class Reading
{
    public required string Status { get; init; }

    public double? Value { get; init; }

    public required string Unit { get; init; }

    public string? SensorId { get; init; }

    public string? SourceName { get; init; }

    public required string Binding { get; init; }

    public bool Verified { get; init; }

    public bool OperatorAsserted { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public double? LibraryValue { get; init; }

    public string? LibraryUnit { get; init; }

    public ReadingError? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Inputs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Formula { get; init; }
}

public sealed class ReadingList
{
    public required string Status { get; init; }

    public required IReadOnlyList<Reading> Items { get; init; }
}

public sealed class ReadingSet
{
    public CpuReadings Cpu { get; init; } = new();

    public GpuReadings Gpu { get; init; } = new();

    public MemoryReadings Memory { get; init; } = new();

    public FanReadings Fans { get; init; } = new();
}

public sealed class CpuReadings
{
    public Reading Temperature { get; init; } = ReadingFactory.Unmapped("celsius");

    public UtilizationReadings Utilization { get; init; } = new();
}

public sealed class UtilizationReadings
{
    public Reading Total { get; init; } = ReadingFactory.Unmapped("percent");

    public ReadingList Logical { get; init; } = ReadingFactory.UnmappedList();
}

public sealed class GpuReadings
{
    public Reading Temperature { get; init; } = ReadingFactory.Unmapped("celsius");

    public Reading Load { get; init; } = ReadingFactory.Unmapped("percent");

    public VramReadings Vram { get; init; } = new();
}

public sealed class VramReadings
{
    public Reading Used { get; init; } = ReadingFactory.Unmapped("bytes");

    public Reading Total { get; init; } = ReadingFactory.Unmapped("bytes");
}

public sealed class MemoryReadings
{
    public Reading Used { get; init; } = ReadingFactory.Unmapped("bytes");

    public Reading Total { get; init; } = ReadingFactory.Unmapped("bytes");
}

public sealed class FanReadings
{
    public ReadingList Cpu { get; init; } = ReadingFactory.UnmappedList();

    public ReadingList Gpu { get; init; } = ReadingFactory.UnmappedList();

    public ReadingList Case { get; init; } = ReadingFactory.UnmappedList();
}

public sealed class DerivedMemory
{
    public Reading? TotalBytes { get; init; }
}

public static class ReadingFactory
{
    public static Reading Unmapped(string unit)
    {
        return new Reading
        {
            Status = "unmapped",
            Value = null,
            Unit = unit,
            Binding = "none",
            Verified = false,
            OperatorAsserted = false
        };
    }

    public static ReadingList UnmappedList()
    {
        return new ReadingList
        {
            Status = "unmapped",
            Items = []
        };
    }
}

public static class LimitationText
{
    public static readonly IReadOnlyList<string> Always =
    [
        "Polling is read-only. It does not write fan duty, edit Fan Control configuration, install a driver, open a firewall port, or listen on the network.",
        "Read-only does not mean contention-free. Motherboard super I/O reads can contend with Fan Control. The default interval is 1 second. Set motherboardPolling to false if curves glitch.",
        "A fan value of 0 with status ok is a stopped tach. A missing tach is null and its status is missing, stale, unsupported, or error.",
        "CPU load sensors are not guaranteed to include one entry per logical processor. A shared fan header can report one tach for two fans.",
        "Candidate rows are not verified role bindings. verified is false unless a later version has actually checked the physical header or die sensor. This version never sets verified to true.",
        "No claim is made of zero CPU, RAM, or frametime overhead. Measure those on the Windows PC. The Pi client, not this host, owns 60 Hz drawing, warnings, and hysteresis."
    ];

    public const string Synthetic =
        "synthetic is true. Every number in this document is a fixture. It is not a live reading from the Ryzen 7 5800X3D, RTX 2080 SUPER, or Scythe Fuma 2.";
}
