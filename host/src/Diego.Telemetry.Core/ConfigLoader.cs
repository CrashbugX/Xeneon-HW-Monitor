// Loads host.json. Rejects poll intervals under 0.5 seconds.
using System.Text.Json;

namespace Diego.Telemetry;

public static class ConfigLoader
{
    public static HostConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new HostConfig();
        }

        if (!File.Exists(path))
        {
            throw new ConfigException($"Config file was not found: {path}");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Config file could not be read: {ex.Message}");
        }

        return Parse(json);
    }

    public static HostConfig Parse(string json)
    {
        HostConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<HostConfig>(json, JsonFormat.Config);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config JSON could not be parsed: {ex.Message}");
        }

        if (config is null)
        {
            throw new ConfigException("Config JSON was empty.");
        }

        config.Roles ??= new RoleMap();
        config.Roles.CpuUtilizationLogical ??= [];
        config.Roles.FanCpu ??= [];
        config.Roles.FanGpu ??= [];
        config.Roles.FanCase ??= [];
        Validate(config);
        return config;
    }

    public static void Validate(HostConfig config)
    {
        var errors = new List<string>();
        if (config.PollIntervalSeconds < TelemetrySchema.MinimumPollIntervalSeconds
            || config.PollIntervalSeconds > TelemetrySchema.MaximumPollIntervalSeconds)
        {
            errors.Add(
                $"pollIntervalSeconds must be from {TelemetrySchema.MinimumPollIntervalSeconds} to {TelemetrySchema.MaximumPollIntervalSeconds}. " +
                $"{config.PollIntervalSeconds} is outside that range. The floor exists so this host cannot be configured into a 60 Hz sensor poll.");
        }

        if (config.StaleAfterSeconds < config.PollIntervalSeconds
            || config.StaleAfterSeconds > TelemetrySchema.MaximumStaleAfterSeconds)
        {
            errors.Add(
                $"staleAfterSeconds must be from the poll interval ({config.PollIntervalSeconds}) through {TelemetrySchema.MaximumStaleAfterSeconds}.");
        }

        CheckId(errors, "roles.cpuTemperature", config.Roles.CpuTemperature);
        CheckId(errors, "roles.gpuTemperature", config.Roles.GpuTemperature);
        CheckId(errors, "roles.cpuUtilizationTotal", config.Roles.CpuUtilizationTotal);
        CheckId(errors, "roles.gpuLoad", config.Roles.GpuLoad);
        CheckId(errors, "roles.memoryUsed", config.Roles.MemoryUsed);
        CheckId(errors, "roles.memoryTotal", config.Roles.MemoryTotal);
        CheckId(errors, "roles.vramUsed", config.Roles.VramUsed);
        CheckId(errors, "roles.vramTotal", config.Roles.VramTotal);
        CheckList(errors, "roles.cpuUtilizationLogical", config.Roles.CpuUtilizationLogical);
        CheckList(errors, "roles.fanCpu", config.Roles.FanCpu);
        CheckList(errors, "roles.fanGpu", config.Roles.FanGpu);
        CheckList(errors, "roles.fanCase", config.Roles.FanCase);
        if (!EndpointRules.TryValidatePort(config.PublishPort, out var publishError))
        {
            errors.Add("publishPort: " + publishError);
        }

        if (!EndpointRules.TryValidatePort(config.DiscoveryPort, out var discoveryError))
        {
            errors.Add("discoveryPort: " + discoveryError);
        }

        if (errors.Count > 0)
        {
            throw new ConfigException(string.Join(" ", errors));
        }
    }

    private static void CheckList(List<string> errors, string field, IReadOnlyList<string>? ids)
    {
        if (ids is null)
        {
            return;
        }

        if (ids.Count > 256)
        {
            errors.Add($"{field} has {ids.Count} ids. The limit is 256.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            CheckId(errors, $"{field}[{i}]", ids[i]);
            if (!string.IsNullOrWhiteSpace(ids[i]) && !seen.Add(ids[i]))
            {
                errors.Add($"{field} lists {ids[i]} more than once.");
            }
        }
    }

    private static void CheckId(List<string> errors, string field, string? id)
    {
        if (id is null)
        {
            return;
        }

        if (id.Length == 0 || id.Length > 512 || !id.StartsWith('/') || id.Any(char.IsWhiteSpace))
        {
            errors.Add($"{field} must be a sensor id that starts with '/' and contains no whitespace.");
        }
    }
}
