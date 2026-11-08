// Sensor samples passed from a hardware source into the snapshot builder.
namespace Diego.Telemetry;

public enum SampleState
{
    Fresh,
    Missing,
    Stale,
    Unsupported,
    Error
}

public sealed record ReadingError(string Code, string Message);

public sealed record UnitConversion(string LibraryUnit, string NormalizedUnit, double? NormalizedValue);

public sealed record SensorSample
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

    public double? LibraryValue { get; init; }

    public required string LibraryUnit { get; init; }

    public double? NormalizedValue { get; init; }

    public required string NormalizedUnit { get; init; }

    public required SampleState State { get; init; }

    public ReadingError? Error { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record PawnIoReport
{
    public required bool Checked { get; init; }

    public bool? Installed { get; init; }

    public string? DisplayVersion { get; init; }

    public required string Detection { get; init; }
}

public sealed record BackendReport
{
    public required PawnIoReport PawnIo { get; init; }

    public required IReadOnlyDictionary<string, string> Groups { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record HardwareObservation
{
    public required string Kind { get; init; }

    public required bool Synthetic { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    public string? AssemblyVersion { get; init; }

    public required BackendReport Backend { get; init; }

    public required IReadOnlyList<SensorSample> Sensors { get; init; }
}

public interface IHardwareSource : IAsyncDisposable
{
    string Kind { get; }

    bool IsSynthetic { get; }

    Task<HardwareObservation> ReadAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
