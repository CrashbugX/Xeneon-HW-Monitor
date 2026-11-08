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
        FirewallText.Text = "Firewall is unchanged. Inbound TCP 8765 is telemetry. Inbound UDP 8764 is discovery. Private profile, local subnet. Run Repair-PrivateFirewall.ps1 yourself if you change the port.";
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HostSettings.CancelClose(_exit))
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    public void ExitApplication()
    {
        _exit = true;
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

    private void ExitClick(object? sender, RoutedEventArgs e)
    {
        ExitApplication();
    }
}
