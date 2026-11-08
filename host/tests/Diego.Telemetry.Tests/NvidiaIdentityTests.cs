using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Diego.Telemetry.Host;

namespace Diego.Telemetry.Tests;

/// <summary>
/// LibreHardwareMonitorLib 0.9.6 can activate two NVIDIA load sensors with one
/// identifier. These fixtures are not readings from the RTX 2080 SUPER.
/// The 16.4 and 1.0 figures are the pair shown in upstream issue 2499.
/// </summary>
public sealed class NvidiaIdentityTests
{
    private static readonly DateTimeOffset Observed = DateTimeOffset.Parse("2026-11-08T21:30:00Z");

    [Fact]
    public void DuplicateLoadIndexKeepsGpuMemoryAndPublishesGpuBusSeparately()
    {
        var busFirst = Build(CollisionSensors(busBeforeMemory: true));
        var memoryFirst = Build(CollisionSensors(busBeforeMemory: false));

        AssertPair(busFirst);
        AssertPair(memoryFirst);
        Assert.Equal(16.4d, Published(busFirst, "/gpu-nvidia/0/load/3").NormalizedValue);
        Assert.Equal(1.0d, Published(busFirst, "/gpu-nvidia/0/load/3/gpu-bus").NormalizedValue);
        Assert.Equal(16.4d, Published(memoryFirst, "/gpu-nvidia/0/load/3").NormalizedValue);
        Assert.Equal(1.0d, Published(memoryFirst, "/gpu-nvidia/0/load/3/gpu-bus").NormalizedValue);
    }

    [Fact]
    public void ConfiguredBareIdStaysGpuMemoryAndBusIdStaysBus()
    {
        var config = new HostConfig
        {
            Roles = new RoleMap
            {
                GpuLoad = "/gpu-nvidia/0/load/3"
            }
        };
        var document = Build(CollisionSensors(busBeforeMemory: true), config);
        Assert.Equal("ok", document.Readings.Gpu.Load.Status);
        Assert.Equal(16.4d, document.Readings.Gpu.Load.Value);
        Assert.Equal("percent", document.Readings.Gpu.Load.Unit);
        Assert.Equal("/gpu-nvidia/0/load/3", document.Readings.Gpu.Load.SensorId);
        Assert.Equal("GPU Memory", document.Readings.Gpu.Load.SourceName);
        Assert.NotEqual(1.0d, document.Readings.Gpu.Load.Value);

        config.Roles.GpuLoad = "/gpu-nvidia/0/load/3/gpu-bus";
        document = Build(CollisionSensors(busBeforeMemory: false), config);
        Assert.Equal("ok", document.Readings.Gpu.Load.Status);
        Assert.Equal(1.0d, document.Readings.Gpu.Load.Value);
        Assert.Equal("percent", document.Readings.Gpu.Load.Unit);
        Assert.Equal("GPU Bus", document.Readings.Gpu.Load.SourceName);
    }

    [Fact]
    public void OtherGpuAndUntouchedLoadKeepTheirIds()
    {
        var document = Build(CollisionSensors(busBeforeMemory: true));
        var core = Published(document, "/gpu-nvidia/0/load/0");
        var other = Published(document, "/gpu-nvidia/1/load/3");
        Assert.Equal("GPU Core", core.Name);
        Assert.Equal(48d, core.NormalizedValue);
        Assert.Null(core.LibraryId);
        Assert.Equal("GPU Memory", other.Name);
        Assert.Equal(9d, other.NormalizedValue);
        Assert.Null(other.LibraryId);
        Assert.DoesNotContain(document.Identity, note => note.LibraryId == "/gpu-nvidia/1/load/3");
        Assert.DoesNotContain(document.Identity, note => note.LibraryId == "/gpu-nvidia/0/load/0");
    }

    [Fact]
    public void UniqueCandidatesDoNotPickEitherCollidingLoad()
    {
        var document = Build(CollisionSensors(busBeforeMemory: true), new HostConfig { AcceptUniqueCandidates = true });
        Assert.Equal("unmapped", document.Readings.Gpu.Load.Status);
        Assert.Null(document.Readings.Gpu.Load.Value);
        var loads = document.Candidates.Where(candidate => candidate.Role == "gpu.load").Select(candidate => candidate.SensorId).ToArray();
        Assert.Contains("/gpu-nvidia/0/load/3", loads);
        Assert.Contains("/gpu-nvidia/0/load/3/gpu-bus", loads);
        Assert.Contains("/gpu-nvidia/0/load/0", loads);
        Assert.Contains("/gpu-nvidia/1/load/3", loads);
        Assert.All(document.Candidates.Where(candidate => candidate.Role == "gpu.load"), candidate => Assert.False(candidate.Unique));
    }

    [Fact]
    public void PowerRowThatLandsOnTheMemoryIndexIsNotRelabeled()
    {
        var sensors = new[]
        {
            Nvidia("0", "load", 3, "GPU Power", "Load", 55),
            Nvidia("0", "load", 3, "GPU Memory", "Load", 16.4)
        };
        var document = Build(sensors);
        var memory = Published(document, "/gpu-nvidia/0/load/3");
        var power = Published(document, "/gpu-nvidia/0/load/3/gpu-power");
        Assert.Equal("GPU Memory", memory.Name);
        Assert.Equal(16.4d, memory.NormalizedValue);
        Assert.Equal("percent", memory.NormalizedUnit);
        Assert.Equal("GPU Power", power.Name);
        Assert.Equal(55d, power.NormalizedValue);
        Assert.Equal("percent", power.NormalizedUnit);
        Assert.Equal("percent", power.LibraryUnit);
    }

    [Fact]
    public void DistinctSmallDataNamesKeepBytesAndDoNotClaimTheBareId()
    {
        var sensors = new[]
        {
            Nvidia("0", "smalldata", 3, "D3D Dedicated Memory Used", "SmallData", 3400),
            Nvidia("0", "smalldata", 3, "GPU Memory Shared Extra", "SmallData", 100)
        };
        var config = new HostConfig
        {
            Roles = new RoleMap { VramUsed = "/gpu-nvidia/0/smalldata/3" }
        };
        var document = Build(sensors, config);
        Assert.Equal("error", document.Readings.Gpu.Vram.Used.Status);
        Assert.Null(document.Readings.Gpu.Vram.Used.Value);
        Assert.Equal("bytes", document.Readings.Gpu.Vram.Used.Unit);
        Assert.Equal("ambiguous_sensor_identity", document.Readings.Gpu.Vram.Used.Error!.Code);

        var dedicated = Published(document, "/gpu-nvidia/0/smalldata/3/d3d-dedicated-memory-used");
        var extra = Published(document, "/gpu-nvidia/0/smalldata/3/gpu-memory-shared-extra");
        Assert.Equal(3400d * 1024d * 1024d, dedicated.NormalizedValue);
        Assert.Equal(100d * 1024d * 1024d, extra.NormalizedValue);
        Assert.Equal("bytes", dedicated.NormalizedUnit);
        Assert.Equal("bytes", extra.NormalizedUnit);
        Assert.Equal("distinct-names", Assert.Single(document.Identity).Resolution);
        Assert.False(Assert.Single(document.Identity).BareIdAssigned);
    }

    [Fact]
    public void IdenticalNamesPublishNoValueForEitherReading()
    {
        var sensors = new[]
        {
            Nvidia("0", "load", 3, "GPU Memory", "Load", 10),
            Nvidia("0", "load", 3, "GPU Memory", "Load", 20)
        };
        var config = new HostConfig { Roles = new RoleMap { GpuLoad = "/gpu-nvidia/0/load/3" } };
        var document = Build(sensors, config);
        Assert.Equal(2, document.Inventory.Count);
        Assert.All(document.Inventory, row =>
        {
            Assert.NotEqual("/gpu-nvidia/0/load/3", row.Id);
            Assert.StartsWith("/gpu-nvidia/0/load/3/unresolved-", row.Id, StringComparison.Ordinal);
            Assert.Equal("error", row.Status);
            Assert.Null(row.NormalizedValue);
            Assert.Equal("ambiguous_sensor_identity", row.Error!.Code);
            Assert.Equal("percent", row.NormalizedUnit);
        });
        Assert.Contains(document.Inventory, row => row.LibraryValue == 10d);
        Assert.Contains(document.Inventory, row => row.LibraryValue == 20d);
        Assert.Equal("error", document.Readings.Gpu.Load.Status);
        Assert.Null(document.Readings.Gpu.Load.Value);
        Assert.Equal("ambiguous_sensor_identity", document.Readings.Gpu.Load.Error!.Code);
        Assert.DoesNotContain(document.Inventory, row => row.NormalizedValue is 10d or 20d);
        Assert.Equal("ambiguous", Assert.Single(document.Identity).Resolution);
        Assert.False(Assert.Single(document.Identity).BareIdAssigned);
    }

    [Fact]
    public void EmptyLibraryIdDoesNotCrashTheDocument()
    {
        var sensors = new[]
        {
            Nvidia("0", "load", 0, "GPU Core", "Load", 48) with { Id = "", Path = [] }
        };
        var document = Build(sensors);
        var row = Assert.Single(document.Inventory);
        Assert.Equal("error", row.Status);
        Assert.Null(row.NormalizedValue);
        Assert.Equal("missing_library_id", row.Error!.Code);
        Assert.StartsWith("/unidentified/missing-id-", row.Id, StringComparison.Ordinal);
        var json = JsonFormat.Serialize(document, indented: true);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("1.0.0", parsed.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task ListSnapshotWatchAndServeAllReturnTheSplitInventory()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xeneon-identity-" + Guid.NewGuid().ToString("N")));
        var listPath = Path.Combine(directory.FullName, "sensors.json");
        var snapshotPath = Path.Combine(directory.FullName, "snapshot.json");
        var watchPath = Path.Combine(directory.FullName, "watch.jsonl");
        var tokenPath = Path.Combine(directory.FullName, "telemetry.token");
        await File.WriteAllTextAsync(tokenPath, "identity-fixture-token\n");
        var port = FreePort();

        try
        {
            var listCode = await CliApplication.RunAsync(
                ["list-sensors", "--output", listPath],
                Text(),
                Factory,
                new SystemClock(),
                CancellationToken.None);
            Assert.Equal(0, listCode);
            Assert.True(File.Exists(listPath));
            AssertSplitJson(await File.ReadAllTextAsync(listPath));

            var snapshotCode = await CliApplication.RunAsync(
                ["snapshot", "--output", snapshotPath],
                Text(),
                Factory,
                new SystemClock(),
                CancellationToken.None);
            Assert.Equal(0, snapshotCode);
            AssertSplitJson(await File.ReadAllTextAsync(snapshotPath));

            var watchCode = await CliApplication.RunAsync(
                ["watch", "--count", "1", "--output", watchPath],
                Text(),
                Factory,
                new SystemClock(),
                CancellationToken.None);
            Assert.Equal(0, watchCode);
            var watchLine = (await File.ReadAllTextAsync(watchPath)).Trim();
            Assert.DoesNotContain('\n', watchLine);
            AssertSplitJson(watchLine);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var serve = CliApplication.RunAsync(
                ["serve", "--listen", "127.0.0.1", "--port", port.ToString(), "--token-file", tokenPath],
                Text(),
                Factory,
                new SystemClock(),
                cts.Token);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/telemetry");
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer identity-fixture-token");
                    response = await http.SendAsync(request);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(100);
            }

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertSplitJson(await response.Content.ReadAsStringAsync());
            response.Dispose();
            cts.Cancel();
            var serveCode = await serve;
            Assert.Equal(0, serveCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void AssertPair(TelemetryDocument document)
    {
        Assert.Equal(document.Inventory.Count, document.Inventory.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        var memory = Published(document, "/gpu-nvidia/0/load/3");
        var bus = Published(document, "/gpu-nvidia/0/load/3/gpu-bus");
        Assert.Equal("GPU Memory", memory.Name);
        Assert.Equal("GPU Bus", bus.Name);
        Assert.Null(memory.LibraryId);
        Assert.Equal("/gpu-nvidia/0/load/3", bus.LibraryId);
        Assert.Equal("percent", memory.NormalizedUnit);
        Assert.Equal("percent", bus.NormalizedUnit);
        Assert.Equal("ok", memory.Status);
        Assert.Equal("ok", bus.Status);
        var note = Assert.Single(document.Identity, item => item.LibraryId == "/gpu-nvidia/0/load/3");
        Assert.Equal("reserved-owner", note.Resolution);
        Assert.True(note.BareIdAssigned);
        Assert.Contains("GPU Bus", note.Message, StringComparison.Ordinal);
        Assert.Contains("GPU Memory", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("upgrade", note.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("install the", note.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Backend.Warnings, warning => warning.Contains("/gpu-nvidia/0/load/3", StringComparison.Ordinal));
        Assert.Contains(document.Candidates, candidate => candidate.SensorId == bus.Id && candidate.Role == "gpu.load" && candidate.Verified == false);
    }

    private static void AssertSplitJson(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var ids = parsed.RootElement.GetProperty("inventory").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
        Assert.Contains("/gpu-nvidia/0/load/3", ids);
        Assert.Contains("/gpu-nvidia/0/load/3/gpu-bus", ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        var memory = parsed.RootElement.GetProperty("inventory").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "/gpu-nvidia/0/load/3");
        var bus = parsed.RootElement.GetProperty("inventory").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "/gpu-nvidia/0/load/3/gpu-bus");
        Assert.Equal("GPU Memory", memory.GetProperty("name").GetString());
        Assert.Equal("GPU Bus", bus.GetProperty("name").GetString());
        Assert.Equal(16.4d, memory.GetProperty("normalizedValue").GetDouble());
        Assert.Equal(1.0d, bus.GetProperty("normalizedValue").GetDouble());
        Assert.Equal("percent", memory.GetProperty("normalizedUnit").GetString());
        Assert.Equal("percent", bus.GetProperty("normalizedUnit").GetString());
    }

    private static InventorySensor Published(TelemetryDocument document, string id)
    {
        return Assert.Single(document.Inventory, row => row.Id == id);
    }

    private static TelemetryDocument Build(IReadOnlyList<SensorSample> sensors, HostConfig? config = null)
    {
        var rawIds = sensors.Select(sensor => sensor.Id).ToArray();
        if (rawIds.Length != rawIds.Distinct(StringComparer.Ordinal).Count())
        {
            Assert.Throws<ArgumentException>(() => sensors.ToDictionary(sensor => sensor.Id, StringComparer.Ordinal));
        }

        return SnapshotBuilder.Build(Observation(sensors), config ?? new HostConfig(), Observed, sampledOnce: true, sequence: null);
    }

    private static IHardwareSource Factory(HostConfig config, bool synthetic, bool skipWarmup)
    {
        _ = (config, synthetic, skipWarmup);
        return new FixedSource(Observation(CollisionSensors(busBeforeMemory: true, observed: DateTimeOffset.UtcNow)));
    }

    private static StringTextOutput Text()
    {
        return new StringTextOutput(new StringWriter(), new StringWriter());
    }

    private static SensorSample[] CollisionSensors(bool busBeforeMemory, DateTimeOffset? observed = null)
    {
        var at = observed ?? Observed;
        var memory = Nvidia("0", "load", 3, "GPU Memory", "Load", 16.4, at);
        var bus = Nvidia("0", "load", 3, "GPU Bus", "Load", 1.0, at);
        var rows = new List<SensorSample>
        {
            Nvidia("0", "load", 0, "GPU Core", "Load", 48, at)
        };
        if (busBeforeMemory)
        {
            rows.Add(bus);
            rows.Add(memory);
        }
        else
        {
            rows.Add(memory);
            rows.Add(bus);
        }

        rows.Add(Nvidia("1", "load", 3, "GPU Memory", "Load", 9, at));
        return rows.ToArray();
    }

    private static SensorSample Nvidia(string adapter, string channel, int index, string name, string sensorType, double? value, DateTimeOffset? observed = null)
    {
        var hardwareId = "/gpu-nvidia/" + adapter;
        var id = $"{hardwareId}/{channel}/{index}";
        var converted = SensorUnits.Convert(sensorType, value);
        return new SensorSample
        {
            Id = id,
            Path = id.Split('/', StringSplitOptions.RemoveEmptyEntries),
            HardwareId = hardwareId,
            HardwareName = "FIXTURE GPU",
            HardwareType = "GpuNvidia",
            Name = name,
            SensorType = sensorType,
            Index = index,
            LibraryValue = value,
            LibraryUnit = converted.LibraryUnit,
            NormalizedValue = converted.NormalizedValue,
            NormalizedUnit = converted.NormalizedUnit,
            State = SampleState.Fresh,
            ObservedAtUtc = observed ?? Observed
        };
    }

    private static HardwareObservation Observation(IReadOnlyList<SensorSample> sensors)
    {
        return new HardwareObservation
        {
            Kind = "synthetic",
            Synthetic = true,
            Backend = new BackendReport
            {
                PawnIo = new PawnIoReport
                {
                    Checked = false,
                    Installed = null,
                    DisplayVersion = null,
                    Detection = "Fixture. PawnIO was not queried."
                },
                Groups = new Dictionary<string, string> { ["gpu"] = "fixture" },
                Warnings = []
            },
            Sensors = sensors
        };
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FixedSource(HardwareObservation observation) : IHardwareSource
    {
        public string Kind => observation.Kind;

        public bool IsSynthetic => observation.Synthetic;

        public Task<HardwareObservation> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(observation);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StringTextOutput(StringWriter stdout, StringWriter stderr) : ITextOutput
    {
        public TextWriter Out => stdout;

        public TextWriter Error => stderr;
    }
}
