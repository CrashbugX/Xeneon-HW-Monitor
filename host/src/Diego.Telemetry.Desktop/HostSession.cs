// Owns the collector, the listener, and discovery for the desktop window.
using Diego.Telemetry.Fake;
using Diego.Telemetry.Host;
using CoreProgram = Diego.Telemetry.Host.Program;

namespace Diego.Telemetry.Desktop;

/// <summary>
/// One desktop session. If the lock is already held, this object does not
/// open sensors and does not start a second listener.
/// </summary>
public sealed class HostSession : IDisposable
{
    private readonly CollectorOwner _owner;
    private readonly string _directory;
    private TelemetryServer? _server;
    private DiscoveryBeacon? _beacon;
    private IHardwareSource? _source;
    private readonly PairingBroker _pairing;
    public HostConfig Config { get; }

    private HostSession(CollectorOwner owner, string directory, HostConfig config, PairingBroker pairing)
    {
        _owner = owner;
        _directory = directory;
        Config = config;
        _pairing = pairing;
    }

    public string Status { get; private set; } = "";

    public string TelemetryUrl => EndpointRules.TelemetryUrl(Environment.MachineName, Config.PublishPort);

    public string Urls =>
        "Telemetry  " + TelemetryUrl + "\nInventory  " + EndpointRules.InventoryUrl(Environment.MachineName, Config.PublishPort) +
        "\nDiscover   " + EndpointRules.DiscoverUrl(Environment.MachineName, Config.PublishPort);

    public static HostSession Start(string directory)
    {
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "host.json");
        if (!File.Exists(configPath) && File.Exists(ExampleConfigPath()))
        {
            File.Copy(ExampleConfigPath(), configPath);
        }

        ConfigMigration.MigrateFile(configPath);
        var config = File.Exists(configPath) ? ConfigLoader.Load(configPath) : new HostConfig { AutoMap = true };
        var pairing = new PairingBroker(new SystemClock());
        pairing.Load(Path.Combine(directory, "devices.json"));
        var owner = CollectorOwner.TryAcquire(Path.Combine(directory, "collector.lock"));
        var session = new HostSession(owner, directory, config, pairing);
        if (!owner.Acquired)
        {
            session.Status = "Another Xeneon HW Monitor window or the Windows service already owns sensor collection. This window will not open a second collector.";
            return session;
        }

        session.StartPublisher();
        return session;
    }

    public string PendingText()
    {
        var pending = _pairing.Pending;
        if (pending.Count == 0)
        {
            return "No pairing request. On the Pi, choose this PC and the code will show here.";
        }

        return string.Join("\n", pending.Select(item =>
            item.Name + "  code " + item.Code + (item.Approved ? "  approved" : "  waiting for approval") +
            "  until " + item.ExpiresAtUtc.ToString("u")));
    }

    public string FirewallNote()
    {
        return "Firewall is unchanged by this window. Inbound TCP " + Config.PublishPort
            + " carries pairing and telemetry. Inbound UDP " + Config.DiscoveryPort
            + " receives discovery queries. Both are needed only on the Private profile, local subnet. Public stays closed. A manual address on the Pi uses TCP and still works if UDP is blocked. Run Repair-PrivateFirewall.ps1 yourself, elevated, after a port change. Poll interval is in seconds, from 0.5 to 3600.";
    }

    public IReadOnlyList<PairedDevice> Paired => _pairing.Devices;

    public string DeviceText()
    {
        var devices = _pairing.Devices;
        if (devices.Count == 0)
        {
            return "No paired devices.";
        }

        return string.Join("\n", devices.Select(device => device.Name + "  " + device.Id + "  last seen " + (device.LastSeenUtc?.ToString("u") ?? "never")));
    }

    public string ApplyPoll(double seconds)
    {
        var error = HostSettings.ApplyPoll(Config, seconds);
        if (error.Length > 0)
        {
            return error;
        }

        SaveConfig();
        return "Poll interval saved. The next sample uses it.";
    }

    public string ApplyPort(int port)
    {
        var previous = Config.PublishPort;
        var error = HostSettings.ApplyPort(Config, port);
        if (error.Length > 0)
        {
            return error;
        }

        if (!_owner.Acquired)
        {
            Config.PublishPort = previous;
            return "The other collector owns the port. Stop it before changing the port here.";
        }

        SaveConfig();
        StopPublisher();
        try
        {
            StartPublisher();
        }
        catch (Exception ex)
        {
            Config.PublishPort = previous;
            SaveConfig();
            StartPublisher();
            return "Could not bind " + port + ". Restored " + previous + ". " + ex.Message;
        }

        return "Listening on TCP " + Config.PublishPort + ". Discovery is still UDP " + Config.DiscoveryPort
            + ". The firewall was not changed. If the Pi cannot connect, run Repair-PrivateFirewall.ps1 from an elevated PowerShell. This window does not open that prompt.";
    }

    public string ApproveFirstPending()
    {
        var pending = _pairing.Pending.FirstOrDefault();
        if (pending is null)
        {
            return "Nothing to approve.";
        }

        var error = _pairing.Approve(pending.Id);
        return error?.Message ?? "Approved. Enter the code on the Pi.";
    }

    public string Revoke(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "Enter a device id.";
        }

        _pairing.Revoke(deviceId.Trim());
        _pairing.Save(Path.Combine(_directory, "devices.json"));
        return "Revoked if that id was paired. The token hash was removed.";
    }

    public void Stop()
    {
        StopPublisher();
        _pairing.Save(Path.Combine(_directory, "devices.json"));
        _owner.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    private void StartPublisher()
    {
        _source = OpenSource();
        _server = TelemetryServer.Start("0.0.0.0", Config.PublishPort, "", allowLan: true, _source, Config, new SystemClock(), _pairing);
        if (Config.DiscoveryEnabled)
        {
            _beacon = new DiscoveryBeacon(Config.DiscoveryPort, () => new DiscoveryAnnouncement
            {
                Name = Environment.MachineName,
                TelemetryPort = Config.PublishPort,
                Product = ProductInfo.Name,
                Version = ProductInfo.Version
            });
        }

        Status = _source.IsSynthetic
            ? "Publishing a fixture because this process has no LibreHardwareMonitor build. It is not the Windows tray."
            : "Publishing read-only telemetry on port " + Config.PublishPort + ". Fan duty is not written.";
    }

    private void StopPublisher()
    {
        _beacon?.Dispose();
        _beacon = null;
        if (_server is not null)
        {
            _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _server = null;
            _source = null;
        }
    }

    private void SaveConfig()
    {
        var path = Path.Combine(_directory, "host.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(Config, JsonFormat.Pretty) + Environment.NewLine);
    }

    private static IHardwareSource OpenSource()
    {
        try
        {
            return CoreProgram.CreateSource(new HostConfig(), synthetic: false, skipWarmup: false);
        }
        catch (RealHardwareUnavailableException)
        {
            return new SyntheticHardwareSource();
        }
    }

    private static string ExampleConfigPath()
    {
        var nextToApp = Path.Combine(AppContext.BaseDirectory, "config", "example.host.json");
        if (File.Exists(nextToApp))
        {
            return nextToApp;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "example.host.json"));
    }
}
