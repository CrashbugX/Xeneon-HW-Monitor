using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Diego.Telemetry.Desktop;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public static HostSession? PendingSession { get; set; }

    /// <summary>Set before startup when the logon task used --autostart.</summary>
    public static bool StartHidden { get; set; }

    public static string? DeniedMessage { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!string.IsNullOrEmpty(DeniedMessage))
            {
                desktop.MainWindow = new Window
                {
                    Title = "Xeneon HW Monitor",
                    Width = 560,
                    Height = 240,
                    Content = new TextBlock
                    {
                        Text = DeniedMessage,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(24)
                    }
                };
                base.OnFrameworkInitializationCompleted();
                return;
            }

            Window = new MainWindow();
            if (PendingSession is not null)
            {
                Window.Attach(PendingSession);
            }

            desktop.MainWindow = Window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (StartHidden)
            {
                Window.Opened += (_, _) => Window.Hide();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenFromTray(object? sender, EventArgs e)
    {
        Window?.Show();
        Window?.Activate();
    }

    private void ExitFromTray(object? sender, EventArgs e)
    {
        Window?.ExitApplication();
    }

    private void ToggleAutostartFromTray(object? sender, EventArgs e)
    {
        if (sender is NativeMenuItem item)
        {
            Window?.ApplyAutostart(item.IsChecked);
        }
    }

    private void CheckFromTray(object? sender, EventArgs e)
    {
        Window?.Show();
        Window?.Activate();
        Window?.CheckUpdates(sender, new Avalonia.Interactivity.RoutedEventArgs());
    }

    public void SetUpdateMenu(string? version)
    {
        var item = FindTrayItem("Check") ?? FindTrayItem("Update");
        if (item is null)
        {
            return;
        }

        item.Header = string.IsNullOrEmpty(version) ? "Check for updates" : "Update to " + version;
    }

    private NativeMenuItem? FindTrayItem(string startsWith)
    {
        var icons = TrayIcon.GetIcons(this);
        if (icons is null)
        {
            return null;
        }

        foreach (var icon in icons)
        {
            if (icon.Menu is null)
            {
                continue;
            }

            foreach (var entry in icon.Menu.Items)
            {
                if (entry is NativeMenuItem item && item.Header?.ToString()?.StartsWith(startsWith, StringComparison.Ordinal) == true)
                {
                    return item;
                }
            }
        }

        return null;
    }
}
