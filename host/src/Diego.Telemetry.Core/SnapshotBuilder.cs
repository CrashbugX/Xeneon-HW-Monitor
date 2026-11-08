// Maps a hardware observation and the role config into the schema 1.0.0 JSON document.
namespace Diego.Telemetry;

public static class SnapshotBuilder
{
    public static TelemetryDocument Build(
        HardwareObservation observation,
        HostConfig config,
        DateTimeOffset sampledAtUtc,
        bool sampledOnce,
        int? sequence)
    {
        var byId = observation.Sensors.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var (candidates, ambiguous) = CandidateFinder.Find(observation.Sensors);
        var candidatesByRole = candidates.GroupBy(c => c.Role, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SensorCandidate>)g.ToArray(), StringComparer.Ordinal);

        var limitations = observation.Synthetic
            ? LimitationText.Always.Append(LimitationText.Synthetic).ToArray()
            : LimitationText.Always;

        var derivedMemoryTotal = DeriveMemoryTotal(observation.Sensors, sampledAtUtc, config);
        var memoryTotal = config.Roles.MemoryTotal is null
            ? derivedMemoryTotal ?? ReadingFactory.Unmapped("bytes")
            : MapOne(config.Roles.MemoryTotal, "bytes", byId, sampledAtUtc, config, "configured");

        return new TelemetryDocument
        {
            SchemaVersion = TelemetrySchema.Version,
            SampledOnce = sampledOnce,
            Synthetic = observation.Synthetic,
            Sequence = sequence,
            SampledAtUtc = sampledAtUtc,
            Source = new SourceInfo
            {
                Kind = observation.Kind,
                Synthetic = observation.Synthetic,
                PackageId = observation.PackageId,
                PackageVersion = observation.PackageVersion,
                AssemblyVersion = observation.AssemblyVersion
            },
            Poll = new PollInfo
            {
                IntervalSeconds = config.PollIntervalSeconds,
                MinimumIntervalSeconds = TelemetrySchema.MinimumPollIntervalSeconds,
                StaleAfterSeconds = config.StaleAfterSeconds,
                MotherboardPolling = config.MotherboardPolling,
                AcceptUniqueCandidates = config.AcceptUniqueCandidates
            },
            Backend = observation.Backend,
            Limitations = limitations,
            Inventory = observation.Sensors.Select(s => ToInventory(s, sampledAtUtc, config)).ToArray(),
            Candidates = candidates,
            AmbiguousFans = ambiguous,
            Readings = new ReadingSet
            {
                Cpu = new CpuReadings
                {
                    Temperature = MapRole("cpu.temperature", config.Roles.CpuTemperature, "celsius", byId, candidatesByRole, sampledAtUtc, config),
                    Utilization = new UtilizationReadings
                    {
                        Total = MapRole("cpu.utilization.total", config.Roles.CpuUtilizationTotal, "percent", byId, candidatesByRole, sampledAtUtc, config),
                        Logical = MapList(config.Roles.CpuUtilizationLogical, "percent", byId, sampledAtUtc, config)
                    }
                },
                Gpu = new GpuReadings
                {
                    Temperature = MapRole("gpu.temperature", config.Roles.GpuTemperature, "celsius", byId, candidatesByRole, sampledAtUtc, config),
                    Load = MapRole("gpu.load", config.Roles.GpuLoad, "percent", byId, candidatesByRole, sampledAtUtc, config),
                    Vram = new VramReadings
                    {
                        Used = MapRole("vram.used", config.Roles.VramUsed, "bytes", byId, candidatesByRole, sampledAtUtc, config),
                        Total = MapRole("vram.total", config.Roles.VramTotal, "bytes", byId, candidatesByRole, sampledAtUtc, config)
                    }
                },
                Memory = new MemoryReadings
                {
                    Used = MapRole("memory.used", config.Roles.MemoryUsed, "bytes", byId, candidatesByRole, sampledAtUtc, config),
                    Total = memoryTotal
                },
                Fans = new FanReadings
                {
                    Cpu = MapList(config.Roles.FanCpu, "rpm", byId, sampledAtUtc, config),
                    Gpu = MapList(config.Roles.FanGpu, "rpm", byId, sampledAtUtc, config),
                    Case = MapList(config.Roles.FanCase, "rpm", byId, sampledAtUtc, config)
                }
            },
            Derived = new DerivedMemory
            {
                TotalBytes = derivedMemoryTotal
            }
        };
    }

    private static Reading MapRole(
        string role,
        string? configuredId,
        string unit,
        IReadOnlyDictionary<string, SensorSample> byId,
        IReadOnlyDictionary<string, IReadOnlyList<SensorCandidate>> candidatesByRole,
        DateTimeOffset sampledAtUtc,
        HostConfig config)
    {
        if (configuredId is not null)
        {
            return MapOne(configuredId, unit, byId, sampledAtUtc, config, "configured");
        }

        if (!config.AcceptUniqueCandidates
            || !candidatesByRole.TryGetValue(role, out var candidates)
            || candidates.Count != 1)
        {
            return ReadingFactory.Unmapped(unit);
        }

        return MapOne(candidates[0].SensorId, unit, byId, sampledAtUtc, config, "unique-candidate");
    }

    private static ReadingList MapList(
        IReadOnlyList<string> ids,
        string unit,
        IReadOnlyDictionary<string, SensorSample> byId,
        DateTimeOffset sampledAtUtc,
        HostConfig config)
    {
        if (ids.Count == 0)
        {
            return ReadingFactory.UnmappedList();
        }

        var items = ids.Select(id => MapOne(id, unit, byId, sampledAtUtc, config, "configured")).ToArray();
        var status = items.All(i => i.Status == "ok") ? "ok" : "incomplete";
        return new ReadingList { Status = status, Items = items };
    }

    private static Reading MapOne(
        string id,
        string unit,
        IReadOnlyDictionary<string, SensorSample> byId,
        DateTimeOffset sampledAtUtc,
        HostConfig config,
        string binding)
    {
        if (!byId.TryGetValue(id, out var sample))
        {
            return new Reading
            {
                Status = "missing",
                Value = null,
                Unit = unit,
                SensorId = id,
                Binding = binding,
                Verified = false,
                OperatorAsserted = binding == "configured",
                Error = new ReadingError("sensor_not_in_inventory", $"Sensor id {id} was not in this sample.")
            };
        }

        var state = ApplyStale(sample, sampledAtUtc, config);
        var publishValue = state == SampleState.Fresh ? sample.NormalizedValue : null;
        return new Reading
        {
            Status = StatusName(state),
            Value = publishValue,
            Unit = unit,
            SensorId = sample.Id,
            SourceName = sample.Name,
            Binding = binding,
            Verified = false,
            OperatorAsserted = binding == "configured",
            ObservedAtUtc = sample.ObservedAtUtc,
            LibraryValue = sample.LibraryValue,
            LibraryUnit = sample.LibraryUnit,
            Error = sample.Error
        };
    }

    private static InventorySensor ToInventory(SensorSample sample, DateTimeOffset sampledAtUtc, HostConfig config)
    {
        var state = ApplyStale(sample, sampledAtUtc, config);
        return new InventorySensor
        {
            Id = sample.Id,
            Path = sample.Path,
            HardwareId = sample.HardwareId,
            ParentHardwareId = sample.ParentHardwareId,
            HardwareName = sample.HardwareName,
            HardwareType = sample.HardwareType,
            Name = sample.Name,
            SensorType = sample.SensorType,
            Index = sample.Index,
            Status = StatusName(state),
            LibraryValue = sample.LibraryValue,
            LibraryUnit = sample.LibraryUnit,
            NormalizedValue = state == SampleState.Fresh ? sample.NormalizedValue : null,
            NormalizedUnit = sample.NormalizedUnit,
            ObservedAtUtc = sample.ObservedAtUtc,
            Error = sample.Error
        };
    }

    private static SampleState ApplyStale(SensorSample sample, DateTimeOffset sampledAtUtc, HostConfig config)
    {
        if (sample.State is SampleState.Error or SampleState.Missing or SampleState.Unsupported or SampleState.Stale)
        {
            return sample.State;
        }

        var age = sampledAtUtc - sample.ObservedAtUtc;
        if (age > TimeSpan.FromSeconds(config.StaleAfterSeconds))
        {
            return SampleState.Stale;
        }

        return SampleState.Fresh;
    }

    private static string StatusName(SampleState state)
    {
        return state switch
        {
            SampleState.Fresh => "ok",
            SampleState.Missing => "missing",
            SampleState.Stale => "stale",
            SampleState.Unsupported => "unsupported",
            SampleState.Error => "error",
            _ => "error"
        };
    }

    private static Reading? DeriveMemoryTotal(
        IReadOnlyList<SensorSample> sensors,
        DateTimeOffset sampledAtUtc,
        HostConfig config)
    {
        var used = sensors.Where(s => s.HardwareType == "Memory" && s.SensorType == "Data" && s.Name == "Memory Used").ToArray();
        var available = sensors.Where(s => s.HardwareType == "Memory" && s.SensorType == "Data" && s.Name == "Memory Available").ToArray();
        if (used.Length == 0 && available.Length == 0)
        {
            return null;
        }

        if (used.Length != 1 || available.Length != 1)
        {
            return new Reading
            {
                Status = "error",
                Value = null,
                Unit = "bytes",
                Binding = "derived",
                Verified = false,
                OperatorAsserted = false,
                Formula = "Memory Used + Memory Available",
                Inputs = used.Select(s => s.Id).Concat(available.Select(s => s.Id)).ToArray(),
                Error = new ReadingError(
                    "ambiguous_memory_sensors",
                    "Physical RAM total is Used plus Available. More than one sensor used that exact name, so no total was calculated.")
            };
        }

        var usedState = ApplyStale(used[0], sampledAtUtc, config);
        var availableState = ApplyStale(available[0], sampledAtUtc, config);
        var usedBytes = used[0].NormalizedValue;
        var availableBytes = available[0].NormalizedValue;
        if (usedState != SampleState.Fresh || availableState != SampleState.Fresh
            || usedBytes is null || availableBytes is null)
        {
            return new Reading
            {
                Status = "missing",
                Value = null,
                Unit = "bytes",
                Binding = "derived",
                Verified = false,
                OperatorAsserted = false,
                Formula = "Memory Used + Memory Available, after each value was converted from binary gigabytes (2^30 bytes).",
                Inputs = [used[0].Id, available[0].Id],
                Error = new ReadingError(
                    "incomplete_memory_pair",
                    "Physical RAM total needs a fresh Memory Used sample and a fresh Memory Available sample. LibreHardwareMonitorLib 0.9.6 does not expose a total sensor.")
            };
        }

        return new Reading
        {
            Status = "ok",
            Value = usedBytes + availableBytes,
            Unit = "bytes",
            SensorId = null,
            Binding = "derived",
            Verified = false,
            OperatorAsserted = false,
            ObservedAtUtc = used[0].ObservedAtUtc > available[0].ObservedAtUtc ? used[0].ObservedAtUtc : available[0].ObservedAtUtc,
            Formula = "Memory Used + Memory Available, after each value was converted from binary gigabytes (2^30 bytes). This is not a sensor id. The library already rounded the counters to a float number of binary gigabytes.",
            Inputs = [used[0].Id, available[0].Id]
        };
    }
}
