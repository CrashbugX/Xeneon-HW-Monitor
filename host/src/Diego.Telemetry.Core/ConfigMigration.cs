// Rewrites a 0.1 host.json forward without copying secrets into the new file.
using System.Text.Json;

namespace Diego.Telemetry;

/// <summary>
/// A 0.1 file has roles and poll settings and no <c>configSchema</c>.
/// Migration copies that file to <c>host.json.bak</c> once, then writes 0.2
/// fields. Unknown keys, including anything named token, are dropped.
/// The write is a temp file plus replace. If the replace fails, the original remains.
/// </summary>
public static class ConfigMigration
{
    public static MigrationResult MigrateFile(string path)
    {
        if (!File.Exists(path))
        {
            return new MigrationResult(false, false, "absent");
        }

        string original;
        try
        {
            original = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return new MigrationResult(false, false, ex.Message);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(original);
        }
        catch (JsonException ex)
        {
            return new MigrationResult(false, false, ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            var schema = root.TryGetProperty("configSchema", out var schemaValue) ? schemaValue.GetString() : null;
            if (schema == "0.2")
            {
                return new MigrationResult(false, false, "current");
            }

            var config = ReadKnown(root);
            config.ConfigSchema = "0.2";
            if (!root.TryGetProperty("autoMap", out _))
            {
                config.AutoMap = true;
            }

            var backup = path + ".bak";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            var json = JsonSerializer.Serialize(config, JsonFormat.Pretty);
            if (json.Contains("\"token\"", StringComparison.OrdinalIgnoreCase) && original.Contains("\"token\"", StringComparison.OrdinalIgnoreCase))
            {
                return new MigrationResult(false, true, "refusing to write a token field");
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
            return new MigrationResult(true, File.Exists(backup), "migrated");
        }
    }

    private static HostConfig ReadKnown(JsonElement root)
    {
        var config = new HostConfig();
        if (root.TryGetProperty("pollIntervalSeconds", out var poll) && poll.TryGetDouble(out var seconds))
        {
            config.PollIntervalSeconds = seconds;
        }

        if (root.TryGetProperty("motherboardPolling", out var board))
        {
            config.MotherboardPolling = board.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("acceptUniqueCandidates", out var unique))
        {
            config.AcceptUniqueCandidates = unique.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("staleAfterSeconds", out var stale) && stale.TryGetDouble(out var staleSeconds))
        {
            config.StaleAfterSeconds = staleSeconds;
        }

        if (root.TryGetProperty("autoMap", out var autoMap))
        {
            config.AutoMap = autoMap.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("publishPort", out var port) && port.TryGetInt32(out var publishPort))
        {
            config.PublishPort = publishPort;
        }

        if (root.TryGetProperty("discoveryPort", out var discovery) && discovery.TryGetInt32(out var discoveryPort))
        {
            config.DiscoveryPort = discoveryPort;
        }

        if (root.TryGetProperty("discoveryEnabled", out var enabled))
        {
            config.DiscoveryEnabled = enabled.ValueKind != JsonValueKind.False;
        }

        if (root.TryGetProperty("roles", out var roles))
        {
            config.Roles = new RoleMap
            {
                CpuTemperature = Text(roles, "cpuTemperature"),
                GpuTemperature = Text(roles, "gpuTemperature"),
                CpuUtilizationTotal = Text(roles, "cpuUtilizationTotal"),
                CpuUtilizationLogical = List(roles, "cpuUtilizationLogical"),
                GpuLoad = Text(roles, "gpuLoad"),
                MemoryUsed = Text(roles, "memoryUsed"),
                MemoryTotal = Text(roles, "memoryTotal"),
                VramUsed = Text(roles, "vramUsed"),
                VramTotal = Text(roles, "vramTotal"),
                FanCpu = List(roles, "fanCpu"),
                FanGpu = List(roles, "fanGpu"),
                FanCase = List(roles, "fanCase")
            };
        }

        return config;
    }

    private static string? Text(JsonElement roles, string name)
    {
        if (!roles.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static List<string> List(JsonElement roles, string name)
    {
        if (!roles.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();
    }
}

public sealed record MigrationResult(bool Changed, bool BackupWritten, string Detail);
