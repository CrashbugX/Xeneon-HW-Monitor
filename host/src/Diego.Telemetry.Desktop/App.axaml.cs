using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Diego.Telemetry.Desktop;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public static HostSession? PendingSession { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window = new MainWindow();
            if (PendingSession is not null)
            {
                Window.Attach(PendingSession);
            }
            desktop.MainWindow = Window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
}
