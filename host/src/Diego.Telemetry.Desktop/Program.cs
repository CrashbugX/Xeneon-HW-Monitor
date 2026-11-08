using Avalonia;
using Avalonia.Headless;

namespace Diego.Telemetry.Desktop;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--render-fixture")
        {
            FixtureRenderer.Save(args[1]);
            return 0;
        }

        var directory = args.FirstOrDefault(argument => argument.StartsWith("--data=", StringComparison.Ordinal)) is string data
            ? data["--data=".Length..]
            : DefaultDataDirectory();
        HostSession? session = null;
        try
        {
            session = HostSession.Start(directory);
            App.PendingSession = session;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            session?.Dispose();
        }

        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }

    private static string DefaultDataDirectory()
    {
        var programData = Environment.GetEnvironmentVariable("PROGRAMDATA");
        if (!string.IsNullOrWhiteSpace(programData))
        {
            return Path.Combine(programData, "Xeneon", "HW Monitor Host", "config");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(home, "Xeneon", "HW Monitor Host");
    }
}

public static class FixtureRenderer
{
    public static void Save(string path)
    {
        var app = AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var window = new MainWindow();
        window.ShowCurrentLayout();
        window.Show();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The settings window did not render.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        frame.Save(path);
        window.Close();
        _ = app;
    }
}
