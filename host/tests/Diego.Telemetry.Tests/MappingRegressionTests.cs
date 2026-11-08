namespace Diego.Telemetry.Tests;

/// <summary>
/// Shape of the 0.1.1 Windows sample: physical RAM and virtual memory share
/// library names, GPU Memory and GPU Bus are distinct, board fans are unnamed
/// as CPU or case. Values are the published counters needed to prove the
/// arithmetic. The full inventory file is not in this repository.
/// </summary>
public sealed class MappingRegressionTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-08T22:00:31Z");

    [Fact]
    public void PhysicalRamTotalIgnoresVirtualMemory()
    {
        var document = Build(Auto());
        Assert.Equal("ok", document.Readings.Memory.Total.Status);
        Assert.Equal("bytes", document.Readings.Memory.Total.Unit);
        Assert.Equal(20_957_573_120d + 13_310_451_712d, document.Readings.Memory.Total.Value);
        Assert.Equal(document.Readings.Memory.Total.Value, document.Derived.TotalBytes!.Value);
        Assert.Equal(["/ram/data/0", "/ram/data/1"], document.Readings.Memory.Total.Inputs);
        Assert.DoesNotContain("/vram/data/2", document.Readings.Memory.Total.Inputs!);
        Assert.DoesNotContain("/vram/data/3", document.Readings.Memory.Total.Inputs!);
        Assert.Equal(25_039_929_344d + 21_039_255_552d, document.Derived.VirtualTotalBytes!.Value);
        Assert.NotEqual(document.Readings.Memory.Total.Value, document.Derived.VirtualTotalBytes.Value);
        Assert.Equal("/ram/data/0", document.Readings.Memory.Used.SensorId);
        Assert.Equal(20_957_573_120d, document.Readings.Memory.Used.Value);
    }

    [Fact]
    public void VramCountersAreNotForcedToAddUp()
    {
        var document = Build(Auto());
        const double used = 2_337_275_904d;
        const double free = 6_251_610_112d;
        const double total = 8_589_934_592d;
        Assert.Equal(used, document.Readings.Gpu.Vram.Used.Value);
        Assert.Equal(total, document.Readings.Gpu.Vram.Total.Value);
        Assert.Equal("bytes", document.Readings.Gpu.Vram.Used.Unit);
        Assert.NotEqual(total, used + free);
        Assert.Equal(1_048_576d, total - (used + free));
        Assert.Equal("/gpu-nvidia/0/smalldata/1", document.Readings.Gpu.Vram.Used.SensorId);
        Assert.DoesNotContain(document.Mapping, decision => decision.Role == "vram.used" && decision.SensorId == "/gpu-nvidia/0/smalldata/3");
    }

    [Fact]
    public void SemanticMapSelectsCoreSensorsAndLeavesBoardFansUnassigned()
    {
        var document = Build(Auto());
        Assert.Equal("/amdcpu/0/temperature/2", document.Readings.Cpu.Temperature.SensorId);
        Assert.Equal(59.5d, document.Readings.Cpu.Temperature.Value);
        Assert.Equal("semantic", document.Readings.Cpu.Temperature.Binding);
        Assert.False(document.Readings.Cpu.Temperature.Verified);
        Assert.NotEqual(64.5d, document.Readings.Cpu.Temperature.Value);

        Assert.Equal("/amdcpu/0/load/0", document.Readings.Cpu.Utilization.Total.SensorId);
        Assert.Equal(16, document.Readings.Cpu.Utilization.Logical.Items.Count);
        Assert.Equal("/amdcpu/0/load/2", document.Readings.Cpu.Utilization.Logical.Items[0].SensorId);
        Assert.Equal("/amdcpu/0/load/17", document.Readings.Cpu.Utilization.Logical.Items[15].SensorId);
        Assert.DoesNotContain(document.Readings.Cpu.Utilization.Logical.Items, item => item.SensorId == "/amdcpu/0/load/0" || item.SensorId == "/amdcpu/0/load/1");

        Assert.Equal("/gpu-nvidia/0/temperature/0", document.Readings.Gpu.Temperature.SensorId);
        Assert.Equal(45d, document.Readings.Gpu.Temperature.Value);
        Assert.Equal("/gpu-nvidia/0/load/0", document.Readings.Gpu.Load.SensorId);
        Assert.Equal(3d, document.Readings.Gpu.Load.Value);
        Assert.NotEqual("/gpu-nvidia/0/load/3", document.Readings.Gpu.Load.SensorId);
        Assert.NotEqual("/gpu-nvidia/0/load/3/gpu-bus", document.Readings.Gpu.Load.SensorId);

        Assert.Equal(2, document.Readings.Fans.Gpu.Items.Count);
        Assert.Equal(1043d, document.Readings.Fans.Gpu.Items[0].Value);
        Assert.Equal(1044d, document.Readings.Fans.Gpu.Items[1].Value);
        Assert.Equal("unmapped", document.Readings.Fans.Cpu.Status);
        Assert.Equal("unmapped", document.Readings.Fans.Case.Status);
        Assert.Empty(document.Readings.Fans.Cpu.Items);
        Assert.DoesNotContain(document.AmbiguousFans, fan => fan.SourceName == "Chipset Fan");
        var chipset = Assert.Single(document.FanChoices, choice => choice.SourceName == "Chipset Fan");
        Assert.Equal("chipset", chipset.Classification);
        Assert.Empty(chipset.PossibleRoles);
        Assert.Equal(7, document.FanChoices.Count(choice => choice.Classification == "needs-user"));
        Assert.All(document.FanChoices.Where(choice => choice.Classification == "needs-user"), choice => Assert.False(choice.Verified));
        var stopped = document.Inventory.Single(row => row.Id == "/lpc/nct6798d/0/fan/5");
        Assert.Equal("ok", stopped.Status);
        Assert.Equal(0d, stopped.NormalizedValue);
    }

    [Fact]
    public void ConfiguredIdStillWinsOverTheSemanticChoice()
    {
        var config = Auto();
        config.Roles.CpuTemperature = "/amdcpu/0/temperature/3";
        var document = Build(config);
        Assert.Equal("/amdcpu/0/temperature/3", document.Readings.Cpu.Temperature.SensorId);
        Assert.Equal(64.5d, document.Readings.Cpu.Temperature.Value);
        Assert.Equal("configured", document.Readings.Cpu.Temperature.Binding);
        Assert.False(document.Readings.Cpu.Temperature.Verified);
    }

    private static HostConfig Auto()
    {
        return new HostConfig { AutoMap = true };
    }

    private static TelemetryDocument Build(HostConfig config)
    {
        return SnapshotBuilder.Build(Observation(Fixture()), config, At, sampledOnce: true, sequence: null);
    }

    private static List<SensorSample> Fixture()
    {
        var sensors = new List<SensorSample>
        {
            Row("/amdcpu/0", "Cpu", "AMD Ryzen 7 5800X3D", "temperature", 2, "Core (Tctl/Tdie)", "Temperature", 59.5),
            Row("/amdcpu/0", "Cpu", "AMD Ryzen 7 5800X3D", "temperature", 3, "CCD1 (Tdie)", "Temperature", 64.5),
            Row("/amdcpu/0", "Cpu", "AMD Ryzen 7 5800X3D", "load", 0, "CPU Total", "Load", 6.13),
            Row("/amdcpu/0", "Cpu", "AMD Ryzen 7 5800X3D", "load", 1, "CPU Core Max", "Load", 26.21)
        };
        for (var core = 1; core <= 16; core++)
        {
            sensors.Add(Row("/amdcpu/0", "Cpu", "AMD Ryzen 7 5800X3D", "load", core + 1, "CPU Core #" + core, "Load", 1));
        }

        sensors.Add(Row("/ram", "Memory", "Total Memory", "data", 0, "Memory Used", "Data", 19.518260955810547));
        sensors.Add(Row("/ram", "Memory", "Total Memory", "data", 1, "Memory Available", "Data", 12.396324157714844));
        sensors.Add(Row("/vram", "Memory", "Virtual Memory", "data", 2, "Memory Used", "Data", 23.32025146484375));
        sensors.Add(Row("/vram", "Memory", "Virtual Memory", "data", 3, "Memory Available", "Data", 19.59433364868164));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "temperature", 0, "GPU Core", "Temperature", 45));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "temperature", 2, "GPU Hot Spot", "Temperature", 60.09375));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "load", 0, "GPU Core", "Load", 3));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "load", 3, "GPU Memory", "Load", 27.211380004882812));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "load", 3, "GPU Bus", "Load", 1) with { Id = "/gpu-nvidia/0/load/3/gpu-bus" });
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "smalldata", 1, "GPU Memory Used", "SmallData", 2229));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "smalldata", 0, "GPU Memory Free", "SmallData", 5962));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "smalldata", 2, "GPU Memory Total", "SmallData", 8192));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "smalldata", 3, "D3D Dedicated Memory Used", "SmallData", 2033));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "fan", 1, "GPU Fan 1", "Fan", 1043));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "fan", 2, "GPU Fan 2", "Fan", 1044));
        sensors.Add(Row("/gpu-nvidia/0", "GpuNvidia", "GPU", "control", 1, "GPU Fan 1", "Control", 29));
        for (var fan = 0; fan < 7; fan++)
        {
            var rpm = fan >= 5 ? 0d : 800d + fan;
            sensors.Add(Row("/lpc/nct6798d/0", "SuperIO", "Board", "fan", fan, "Fan #" + (fan + 1), "Fan", rpm));
        }

        sensors.Add(Row("/lpc/ec", "EmbeddedController", "EC", "fan", 0, "Chipset Fan", "Fan", 3092));
        return sensors;
    }

    private static SensorSample Row(
        string hardwareId,
        string hardwareType,
        string hardwareName,
        string channel,
        int index,
        string name,
        string sensorType,
        double? value)
    {
        var id = $"{hardwareId}/{channel}/{index}";
        var converted = SensorUnits.Convert(sensorType, value);
        return new SensorSample
        {
            Id = id,
            Path = id.Split('/', StringSplitOptions.RemoveEmptyEntries),
            HardwareId = hardwareId,
            HardwareName = hardwareName,
            HardwareType = hardwareType,
            Name = name,
            SensorType = sensorType,
            Index = index,
            LibraryValue = value,
            LibraryUnit = converted.LibraryUnit,
            NormalizedValue = converted.NormalizedValue,
            NormalizedUnit = converted.NormalizedUnit,
            State = SampleState.Fresh,
            ObservedAtUtc = At
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
                PawnIo = new PawnIoReport { Checked = false, Detection = "Redacted fixture. No driver was queried." },
                Groups = new Dictionary<string, string>(),
                Warnings = []
            },
            Sensors = sensors
        };
    }
}
