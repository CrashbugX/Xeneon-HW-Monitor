using Avalonia.Controls;
using Avalonia.Interactivity;
using Diego.Telemetry;

namespace Diego.Telemetry.Desktop;

public partial class MainWindow : Window
{
    private bool _exit;
    private HostSession? _session;

    public MainWindow()
    {
        InitializeComponent();
        AdvisoryText.Text = "Discovery tells the local network this PC's name and port. It does not include a pairing token. A device is paired only after you approve it here and the code matches.";
        FirewallText.Text = "The firewall is not opened by this window.";
    }

    public void Attach(HostSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>
    /// Fills the same controls a signed-in session shows. Used only by --render-fixture.
    /// Normal startup calls Attach and never this method.
    /// </summary>
    public void ShowCurrentLayout()
    {
        StatusText.Text = "Publishing read-only telemetry on port 8765. Fan duty is not written.";
        PollBox.Text = "1";
        PortBox.Text = "8765";
        UrlText.Text = "Telemetry  http://this-pc:8765/v1/telemetry\nInventory  http://this-pc:8765/v1/inventory\nDiscover   http://this-pc:8765/v1/discover";
        PendingText.Text = "telemetry-pi is waiting. Code 482913. It expires in two minutes. Nothing is paired until you approve.";
        FirewallText.Text = "The firewall rules for TCP 8765 and UDP 8764 are missing. Public stays closed.";
        FixFirewallButton.IsVisible = true;
        AutostartBox.IsChecked = true;
        PiShutdownBox.IsChecked = true;
        UpdateText.Text = "0.3.0 is installed. 0.3.1 is available.";
        UpdateNotes.Text = "Example notes. Update now downloads the installer, checks its SHA-256, and upgrades in place. It does not run until you click it.";
        UpdateButton.IsEnabled = true;
        DeviceBox.Items.Clear();
        DeviceBox.Items.Add(new ComboBoxItem { Content = "telemetry-pi · last seen 2026-11-08 22:00:00Z", Tag = "device-telemetry-pi" });
        DeviceBox.SelectedIndex = 0;
        CopyStatus.Text = "";
    }

    public void Refresh()
    {
        if (_session is null)
        {
            return;
        }

        StatusText.Text = _session.Status;
        PollBox.Text = _session.Config.PollIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PortBox.Text = _session.Config.PublishPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UrlText.Text = _session.Urls;
        FirewallText.Text = _session.FirewallNote();
        var rules = WindowsHostOps.RulesPresent(_session.Config.PublishPort, _session.Config.DiscoveryPort);
        if (!rules && OperatingSystem.IsWindows())
        {
            FirewallText.Text = "The firewall rules for TCP " + _session.Config.PublishPort
                + " and UDP " + _session.Config.DiscoveryPort + " are missing. " + _session.FirewallNote();
        }

        FixFirewallButton.IsVisible = !rules || !OperatingSystem.IsWindows();
        AutostartBox.IsChecked = _session.Config.StartWithWindows || WindowsHostOps.LogonTaskExists();
        PiShutdownBox.IsChecked = _session.Config.PowerOffPiOnShutdown;
        PendingText.Text = _session.PendingText();
        var selected = (DeviceBox.SelectedItem as ComboBoxItem)?.Tag as string;
        DeviceBox.Items.Clear();
        foreach (var device in _session.Paired)
        {
            var seen = device.LastSeenUtc?.ToString("u") ?? "never";
            DeviceBox.Items.Add(new ComboBoxItem
            {
                Content = device.Name + " · last seen " + seen,
                Tag = device.Id
            });
        }

        if (selected is not null)
        {
            foreach (var item in DeviceBox.Items)
            {
                if (item is ComboBoxItem box && box.Tag as string == selected)
                {
                    DeviceBox.SelectedItem = box;
                }
            }
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (OperatingSystem.IsWindows())
        {
            ListenForWindowsShutdown();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ListenForWindowsShutdown()
    {
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnSessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        // Restart uses the same reason as shutdown. Logoff does not power off the Pi.
        if (e.Reason != Microsoft.Win32.SessionEndReasons.SystemShutdown)
        {
            return;
        }

        _session?.BeginCoordinatedShutdown();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.OSShutdown)
        {
            _session?.BeginCoordinatedShutdown();
            base.OnClosing(e);
            return;
        }

        if (HostSettings.CancelClose(_exit))
        {
            e.Cancel = true;
            Hide();
            ShowTrayHint();
        }

        base.OnClosing(e);
    }

    private void ShowTrayHint()
    {
        if (_session is null || _session.Config.TrayHintShown)
        {
            return;
        }

        _session.MarkTrayHintShown();
        var hint = new Window
        {
            Title = "Xeneon HW Monitor",
            Width = 440,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            Content = new TextBlock
            {
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = "Xeneon HW Monitor is still running in the tray. Open it from the icon. Exit is in that menu."
            }
        };
        hint.Show();
    }

    public void ApplyAutostart(bool enabled)
    {
        CopyStatus.Text = (_session?.SetStartWithWindows(enabled) ?? "") + " " + (enabled ? WindowsHostOps.InstallLogonTask() : WindowsHostOps.RemoveLogonTask());
        Refresh();
    }

    public void ExitApplication()
    {
        _exit = true;
        _session?.MarkExit();
        _session?.Stop();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }

        Close();
    }

    private void ApplyPoll(object? sender, RoutedEventArgs e)
    {
        if (_session is null || !double.TryParse(PollBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            CopyStatus.Text = "Enter a poll interval in seconds.";
            return;
        }

        CopyStatus.Text = _session.ApplyPoll(seconds);
        Refresh();
    }

    private void ApplyPort(object? sender, RoutedEventArgs e)
    {
        if (_session is null || !int.TryParse(PortBox.Text, out var port))
        {
            CopyStatus.Text = "Enter a port.";
            return;
        }

        CopyStatus.Text = _session.ApplyPort(port);
        Refresh();
    }

    private async void CopyUrl(object? sender, RoutedEventArgs e)
    {
        var url = _session?.TelemetryUrl ?? "http://this-pc:8765/v1/telemetry";
        try
        {
            if (Clipboard is not null)
            {
                await Clipboard.SetTextAsync(url);
                CopyStatus.Text = "Copied the telemetry URL. The token is not in it.";
                return;
            }
        }
        catch (Exception)
        {
        }

        CopyStatus.Text = "Clipboard is not available in this session. The URL is shown above.";
    }

    private void ApprovePairing(object? sender, RoutedEventArgs e)
    {
        CopyStatus.Text = _session?.ApproveFirstPending() ?? "No host session.";
        Refresh();
    }

    private void RevokeDevice(object? sender, RoutedEventArgs e)
    {
        var id = (DeviceBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(id))
        {
            CopyStatus.Text = "Choose a paired device in the list. There is no separate id to type.";
            return;
        }

        CopyStatus.Text = _session?.Revoke(id) ?? "No host session.";
        Refresh();
    }

    private void ToggleAutostart(object? sender, RoutedEventArgs e)
    {
        ApplyAutostart(AutostartBox.IsChecked == true);
    }

    private void TogglePiShutdown(object? sender, RoutedEventArgs e)
    {
        CopyStatus.Text = _session?.SetPowerOffPi(PiShutdownBox.IsChecked == true) ?? "No host session.";
    }

    private void FixFirewall(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            CopyStatus.Text = "No host session.";
            return;
        }

        CopyStatus.Text = WindowsHostOps.ApplyFirewall(_session.Config.PublishPort, _session.Config.DiscoveryPort);
        Refresh();
    }

    private UpdateOffer? _offer;

    public async void CheckUpdates(object? sender, RoutedEventArgs e)
    {
        UpdateText.Text = "Checking GitHub releases…";
        UpdateButton.IsEnabled = false;
        try
        {
            _offer = await WindowsHostOps.CheckAsync();
        }
        catch (Exception)
        {
            UpdateText.Text = HostUpdate.OfflineMessage;
            if (Avalonia.Application.Current is App app)
            {
                app.SetUpdateMenu(null);
            }

            return;
        }

        if (_offer is null)
        {
            UpdateText.Text = HostUpdate.CurrentMessage(ProductInfo.Version);
            UpdateNotes.Text = "";
            return;
        }

        UpdateText.Text = ProductInfo.Version + " is installed. " + _offer.Version + " is available.";
        UpdateNotes.Text = _offer.Notes;
        UpdateButton.IsEnabled = _offer.HasChecksum;
        if (Avalonia.Application.Current is App current)
        {
            current.SetUpdateMenu(_offer.Version);
        }
    }

    private async void UpdateNow(object? sender, RoutedEventArgs e)
    {
        if (_offer is null)
        {
            return;
        }

        UpdateButton.IsEnabled = false;
        CopyStatus.Text = "Downloading " + _offer.AssetName + "…";
        var problem = await WindowsHostOps.DownloadAndLaunchAsync(_offer);
        if (problem.Length > 0)
        {
            CopyStatus.Text = problem;
            UpdateButton.IsEnabled = true;
            return;
        }

        CopyStatus.Text = "The installer is starting. This window will close so the files can be replaced.";
        ExitApplication();
    }
}
