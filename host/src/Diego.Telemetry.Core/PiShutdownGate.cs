// Decides whether this process may ask the Pi to power off.
// Tray Exit and closing the window do not. Windows shutdown and restart do.
namespace Diego.Telemetry;

public static class PiShutdownGate
{
    /// <summary>
    /// True only for a Windows shutdown or restart, with the setting on, and not while the user chose Exit.
    /// Logoff is not a shutdown. A dropped telemetry poll is not a shutdown.
    /// </summary>
    public static bool ShouldRun(bool settingEnabled, bool userExit, bool windowsShutdown)
    {
        return settingEnabled && windowsShutdown && !userExit;
    }
}
