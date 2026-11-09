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
        return "Inbound TCP " + Config.PublishPort + " is pairing and telemetry. Inbound UDP " + Config.DiscoveryPort
            + " is discovery. The rules are Private and Domain, this program, local subnet. Public stays closed. "
            + "Fix firewall replaces them. It does not turn the firewall off.";
    }

    public string SetStartWithWindows(bool enabled)
    {
        Config.StartWithWindows = enabled;
        SaveConfig();
        var marker = Path.Combine(_directory, "start-with-windows.off");
        if (enabled)
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }

            return "Start with Windows is on.";
        }

        File.WriteAllText(marker, "off\n");
        return "Start with Windows is off. The logon task is removed on Windows.";
    }

    public string SetPowerOffPi(bool enabled)
    {
        Config.PowerOffPiOnShutdown = enabled;
        SaveConfig();
        return enabled
            ? "When Windows shuts down or restarts, the paired Pi is asked to power off. Exit does not do that."
            : "Pi power-off is off. Windows shutdown will leave the Pi running.";
    }

    public void MarkTrayHintShown()
    {
        if (Config.TrayHintShown)
        {
            return;
        }

        Config.TrayHintShown = true;
        SaveConfig();
    }

    public bool ExitRequested { get; private set; }

    public void MarkExit()
    {
        ExitRequested = true;
    }

    /// <summary>
    /// One coordinated shutdown. A second call does nothing.
    /// Exit and a disabled setting return without contacting the Pi.
    /// </summary>
    public ShutdownResult BeginCoordinatedShutdown()
    {
        if (System.Threading.Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
        {
            return new ShutdownResult(ShutdownOutcome.Aborted, TimeSpan.Zero, 0, 0, "already started");
        }

        if (!PiShutdownGate.ShouldRun(Config.PowerOffPiOnShutdown, ExitRequested, windowsShutdown: true))
        {
            return new ShutdownResult(ShutdownOutcome.NotConfigured, TimeSpan.Zero, 0, 0, ExitRequested ? "exit" : "turned off");
        }

        var targets = _pairing.ShutdownTargets();
        if (targets.Count == 0)
        {
            return new ShutdownResult(ShutdownOutcome.NotConfigured, TimeSpan.Zero, 0, 0, "no paired Pi has registered a control address");
        }

        ShutdownResult last = new(ShutdownOutcome.NotConfigured, TimeSpan.Zero, 0, 0, "none");
        foreach (var target in targets)
        {
            using var channel = new HttpPiShutdownChannel(target.BaseUrl, target.Token, TimeSpan.FromSeconds(ShutdownLimits.DefaultRequestTimeoutSeconds));
            var coordinator = new ShutdownCoordinator(new ShutdownPolicy(), channel, new SystemShutdownClock(), piConfigured: true);
            last = Task.Run(() => coordinator.RunAsync(_ => { }, CancellationToken.None)).GetAwaiter().GetResult();
        }

        return last;
    }

    private int _shutdownStarted;

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
            + ". Click Fix firewall so the Private and Domain rules name this port.";
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
        _server.DevicesChanged = () => _pairing.Save(Path.Combine(_directory, "devices.json"));
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
