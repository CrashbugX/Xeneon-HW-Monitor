using Diego.Telemetry.Fake;

namespace Diego.Telemetry.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        return await CliApplication.RunAsync(
            args,
            new ConsoleTextOutput(),
            CreateSource,
            new SystemClock(),
            cts.Token).ConfigureAwait(false);
    }

    public static IHardwareSource CreateSource(HostConfig config, bool synthetic, bool skipWarmup)
    {
        if (synthetic)
        {
            return new SyntheticHardwareSource();
        }

#if WINDOWS_LHM
        return new Diego.Telemetry.LibreHardwareMonitor.LibreHardwareMonitorSource(config, skipWarmup);
#else
        _ = (config, skipWarmup);
        throw new RealHardwareUnavailableException(
            "Real sensors are compiled only into the Windows win-x64 publish, which references LibreHardwareMonitorLib 0.9.6. " +
            "This process cannot see the Ryzen, the RTX 2080 SUPER, or any fan tach. " +
            "Use --synthetic for the fixture, or publish with host/scripts/publish-win-x64.ps1 and run that build on the Windows PC. " +
            "Nothing in this failure path installs a driver.");
#endif
    }
}
