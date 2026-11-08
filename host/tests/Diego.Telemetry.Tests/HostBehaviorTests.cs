using System.Text.Json;
using Diego.Telemetry.Fake;
using Diego.Telemetry.Host;

namespace Diego.Telemetry.Tests;

public sealed class HostBehaviorTests
{
    [Fact]
    public void BinaryUnitsUsePowersOfTwo()
    {
        var eightGib = SensorUnits.Convert("Data", 8);
        var eightGibFromMebi = SensorUnits.Convert("SmallData", 8192);
        Assert.Equal("binary_gibibyte", eightGib.LibraryUnit);
        Assert.Equal("bytes", eightGib.NormalizedUnit);
        Assert.Equal(8d * 1024d * 1024d * 1024d, eightGib.NormalizedValue);
        Assert.Equal(eightGib.NormalizedValue, eightGibFromMebi.NormalizedValue);
    }

    [Fact]
    public void ZeroFanStaysZeroAndNullTemperatureStaysNull()
    {
        var fan = SensorUnits.Convert("Fan", 0);
        var missing = SensorUnits.Convert("Temperature", null);
        Assert.Equal(0d, fan.NormalizedValue);
        Assert.Equal("rpm", fan.NormalizedUnit);
        Assert.Null(missing.NormalizedValue);
        Assert.Equal("celsius", missing.NormalizedUnit);
    }

    [Fact]
    public void ExampleConfigLoadsAndShortIntervalIsRejected()
    {
        var example = ConfigLoader.Parse(File.ReadAllText(Repo.Find("host/config/example.host.json")));
        Assert.Equal(1d, example.PollIntervalSeconds);
        Assert.True(example.MotherboardPolling);
        Assert.False(example.AcceptUniqueCandidates);
        Assert.Null(example.Roles.CpuTemperature);
        Assert.Empty(example.Roles.FanCpu);

        var bad = new HostConfig { PollIntervalSeconds = 0.1 };
        var error = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(bad));
        Assert.Contains("0.5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmappedRolesAreNullAndAmbiguousFansAreNotAssigned()
    {
        var document = BuildSynthetic(new HostConfig());
        Assert.Equal("1.0.0", document.SchemaVersion);
        Assert.True(document.SampledOnce);
        Assert.True(document.Synthetic);
        Assert.Equal("synthetic", document.Source.Kind);
        Assert.Equal("unmapped", document.Readings.Cpu.Temperature.Status);
        Assert.Null(document.Readings.Cpu.Temperature.Value);
        Assert.Equal("celsius", document.Readings.Cpu.Temperature.Unit);
        Assert.False(document.Readings.Cpu.Temperature.Verified);
        Assert.Equal("unmapped", document.Readings.Fans.Cpu.Status);
        Assert.Empty(document.Readings.Fans.Cpu.Items);
        Assert.Contains(document.AmbiguousFans, fan => fan.SourceName == "CPU Fan" && fan.Verified == false);
        Assert.Contains(document.AmbiguousFans, fan => fan.PossibleRoles.Contains("fan.cpu") && fan.PossibleRoles.Contains("fan.case"));
        Assert.DoesNotContain(document.Candidates, candidate => candidate.Role is "fan.cpu" or "fan.case");
        Assert.Equal(16, document.Candidates.Count(candidate => candidate.Role == "cpu.utilization.logical"));
        Assert.DoesNotContain(document.Candidates, candidate => candidate.SourceName == "CPU Core Max");
        Assert.Contains(document.Limitations, note => note.Contains("contention", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MappedStoppedFanIsZeroAndMissingSensorIsNull()
    {
        var config = new HostConfig
        {
            Roles = new RoleMap
            {
                FanGpu = ["/synthetic/gpu/0/fan/0"],
                FanCase = ["/synthetic/superio/0/fan/2", "/synthetic/does/not/exist"],
                CpuTemperature = "/synthetic/cpu/0/temperature/2"
            }
        };

        var document = BuildSynthetic(config);
        var stopped = Assert.Single(document.Readings.Fans.Gpu.Items);
        Assert.Equal("ok", stopped.Status);
        Assert.Equal(0d, stopped.Value);
        Assert.Equal("rpm", stopped.Unit);
        Assert.True(stopped.OperatorAsserted);
        Assert.False(stopped.Verified);

        Assert.Equal("incomplete", document.Readings.Fans.Case.Status);
        Assert.Equal("missing", document.Readings.Fans.Case.Items[0].Status);
        Assert.Null(document.Readings.Fans.Case.Items[0].Value);
        Assert.Equal("missing", document.Readings.Fans.Case.Items[1].Status);
        Assert.Equal("sensor_not_in_inventory", document.Readings.Fans.Case.Items[1].Error!.Code);

        Assert.Equal("stale", document.Readings.Cpu.Temperature.Status);
        Assert.Null(document.Readings.Cpu.Temperature.Value);
        Assert.Equal("celsius", document.Readings.Cpu.Temperature.Unit);
        Assert.Equal(70d, document.Readings.Cpu.Temperature.LibraryValue);
    }

    [Fact]
    public void AgedFreshSampleBecomesStaleNull()
    {
        var observed = DateTimeOffset.Parse("2026-11-08T19:00:00Z");
        var observation = SyntheticHardwareSource.Build(observed);
        var onlyFresh = observation.Sensors.Select(sample => sample.State == SampleState.Stale
            ? sample with { State = SampleState.Fresh, Error = null }
            : sample).ToArray();
        observation = new HardwareObservation
        {
            Kind = observation.Kind,
            Synthetic = true,
            Backend = observation.Backend,
            Sensors = onlyFresh
        };
        var document = SnapshotBuilder.Build(
            observation,
            new HostConfig { StaleAfterSeconds = 5 },
            observed.AddMinutes(1),
            sampledOnce: true,
            sequence: null);
        var gpuFan = document.Inventory.Single(sensor => sensor.Id == "/synthetic/gpu/0/fan/0");
        Assert.Equal("stale", gpuFan.Status);
        Assert.Null(gpuFan.NormalizedValue);
    }

    [Fact]
    public void UniqueCandidateIsNotDescribedAsVerified()
    {
        var config = new HostConfig { AcceptUniqueCandidates = true };
        var document = BuildSynthetic(config);
        var used = document.Readings.Memory.Used;
        Assert.Equal("unique-candidate", used.Binding);
        Assert.False(used.Verified);
        Assert.False(used.OperatorAsserted);
        Assert.Equal("ok", used.Status);
        Assert.Equal(12.5d * 1024d * 1024d * 1024d, used.Value);
        Assert.Equal("bytes", used.Unit);
        Assert.Equal("unmapped", document.Readings.Cpu.Temperature.Status);
        Assert.Null(document.Readings.Cpu.Temperature.SensorId);
    }

    [Fact]
    public void DerivedMemoryTotalDoesNotInventASensorId()
    {
        var document = BuildSynthetic(new HostConfig());
        var total = document.Readings.Memory.Total;
        Assert.Equal("derived", total.Binding);
        Assert.False(total.Verified);
        Assert.Null(total.SensorId);
        Assert.Equal("ok", total.Status);
        Assert.Equal(32d * 1024d * 1024d * 1024d, total.Value);
        Assert.Equal("bytes", total.Unit);
        Assert.Equal(["/synthetic/ram/data/0", "/synthetic/ram/data/1"], total.Inputs);
        Assert.Equal(total.Value, document.Derived.TotalBytes!.Value);
    }

    [Fact]
    public void LogicalIdsStayInConfigOrderAndDoNotGuessIndexes()
    {
        var config = new HostConfig
        {
            Roles = new RoleMap
            {
                CpuUtilizationLogical =
                [
                    "/synthetic/cpu/0/load/4",
                    "/synthetic/missing/load/0",
                    "/synthetic/cpu/0/load/3"
                ]
            }
        };
        var document = BuildSynthetic(config);
        Assert.Equal("incomplete", document.Readings.Cpu.Utilization.Logical.Status);
        Assert.Equal("/synthetic/cpu/0/load/4", document.Readings.Cpu.Utilization.Logical.Items[0].SensorId);
        Assert.Equal("missing", document.Readings.Cpu.Utilization.Logical.Items[1].Status);
        Assert.Null(document.Readings.Cpu.Utilization.Logical.Items[1].Value);
        Assert.Equal("CPU Core #1 Thread #2", document.Readings.Cpu.Utilization.Logical.Items[2].SourceName);
    }

    [Fact]
    public void NameFragmentsAreNotTotalOrLogicalCandidates()
    {
        var now = DateTimeOffset.Parse("2026-11-08T19:00:00Z");
        var sensors = new[]
        {
            Make("load", 0, "CPU Total Package", "Load", 10, now),
            Make("load", 1, "CPU Core Max", "Load", 10, now),
            Make("load", 2, "CPU Core #1 Thread #1", "Load", 10, now)
        };
        var (candidates, _) = CandidateFinder.Find(sensors);
        Assert.DoesNotContain(candidates, candidate => candidate.Role == "cpu.utilization.total");
        Assert.Single(candidates, candidate => candidate.Role == "cpu.utilization.logical");
    }

    [Fact]
    public async Task WatchCancellationClosesTheSource()
    {
        var source = new BlockingSource();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stop = await new WatchSession().RunAsync(
            source,
            new HostConfig(),
            count: null,
            (_, _) => Task.CompletedTask,
            new SystemClock(),
            cts.Token);
        Assert.Equal(WatchStop.Cancelled, stop);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task CliSyntheticSnapshotIsObviouslyFake()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApplication.RunAsync(
            ["snapshot", "--synthetic"],
            new StringText(stdout, stderr),
            Program.CreateSource,
            new SystemClock(),
            CancellationToken.None);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.True(json.RootElement.GetProperty("synthetic").GetBoolean());
        Assert.True(json.RootElement.GetProperty("sampledOnce").GetBoolean());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("synthetic", json.RootElement.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("readings").GetProperty("cpu").GetProperty("temperature").GetProperty("value").ValueKind);
        Assert.DoesNotContain("\"verified\": true", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"unit\": \"celsius\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"unit\": \"bytes\"", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealHardwareOnThisBuildFailsClosed()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApplication.RunAsync(
            ["snapshot"],
            new StringText(stdout, stderr),
            Program.CreateSource,
            new SystemClock(),
            CancellationToken.None);
        Assert.Equal(3, code);
        Assert.Contains("LibreHardwareMonitorLib 0.9.6", stderr.ToString(), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("real_hardware_unavailable", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CheckBackendSyntheticDoesNotOpenHardware()
    {
        var stdout = new StringWriter();
        var code = await CliApplication.RunAsync(
            ["check-backend", "--synthetic"],
            new StringText(stdout, new StringWriter()),
            Program.CreateSource,
            new SystemClock(),
            CancellationToken.None);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.False(json.RootElement.GetProperty("computerOpenCalled").GetBoolean());
        Assert.True(json.RootElement.GetProperty("synthetic").GetBoolean());
    }

    [Fact]
    public void SchemaPinsTheSameVersionAsTheHost()
    {
        var schemaPath = Repo.Find("protocol/schemas/telemetry-snapshot-1.0.0.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(TelemetrySchema.Version, schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        Assert.Contains("protocol/schemas/telemetry-snapshot-1.0.0.schema.json", schema.RootElement.GetProperty("$id").GetString(), StringComparison.Ordinal);
        Assert.Contains("sampledOnce", schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("synthetic", schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "host", "schemas", "telemetry-snapshot-1.0.0.schema.json")));
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "schemas", "telemetry-snapshot-1.0.0.schema.json")));
        var copies = Directory.GetFiles(Repo.Root(), "telemetry-snapshot-1.0.0.schema.json", SearchOption.AllDirectories);
        Assert.Single(copies);
    }

    private static TelemetryDocument BuildSynthetic(HostConfig config)
    {
        var now = DateTimeOffset.Parse("2026-11-08T19:14:00Z");
        return SnapshotBuilder.Build(
            SyntheticHardwareSource.Build(now),
            config,
            now,
            sampledOnce: true,
            sequence: null);
    }

    private static SensorSample Make(string channel, int index, string name, string type, double? value, DateTimeOffset observed)
    {
        var converted = SensorUnits.Convert(type, value);
        return new SensorSample
        {
            Id = $"/synthetic/cpu/0/{channel}/{index}",
            Path = ["synthetic", "cpu", "0", channel, index.ToString()],
            HardwareId = "/synthetic/cpu/0",
            HardwareName = "SYNTHETIC",
            HardwareType = "Cpu",
            Name = name,
            SensorType = type,
            Index = index,
            LibraryValue = value,
            LibraryUnit = converted.LibraryUnit,
            NormalizedValue = converted.NormalizedValue,
            NormalizedUnit = converted.NormalizedUnit,
            State = SampleState.Fresh,
            ObservedAtUtc = observed
        };
    }

    private sealed class BlockingSource : IHardwareSource
    {
        public bool Disposed { get; private set; }

        public string Kind => "scripted";

        public bool IsSynthetic => true;

        public async Task<HardwareObservation> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The delay should have been cancelled.");
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StringText(StringWriter stdout, StringWriter stderr) : ITextOutput
    {
        public TextWriter Out => stdout;

        public TextWriter Error => stderr;
    }
}

public static class Repo
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var hasProtocol = Directory.Exists(Path.Combine(dir.FullName, "protocol"));
            var hasHost = File.Exists(Path.Combine(dir.FullName, "host", "Diego.Telemetry.slnx"));
            var hasProvenance = File.Exists(Path.Combine(dir.FullName, "PROVENANCE.md"));
            if (hasProtocol && hasHost && hasProvenance)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    public static string Find(string relative)
    {
        return Path.Combine(Root(), relative);
    }
}
