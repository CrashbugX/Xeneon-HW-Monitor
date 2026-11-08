// Validates settings before they are written. A rejected value leaves the previous one.
namespace Diego.Telemetry;

/// <summary>
/// Port and poll changes used by the host window. A rejected value does not
/// replace the stored one. The window restarts the listener after a port
/// change so the new endpoint is the one discovery advertises.
/// </summary>
public static class HostSettings
{
    public static string ApplyPort(HostConfig config, int port)
    {
        if (!EndpointRules.TryValidatePort(port, out var error))
        {
            return error;
        }

        config.PublishPort = port;
        return "";
    }

    public static string ApplyPoll(HostConfig config, double seconds)
    {
        if (seconds < TelemetrySchema.MinimumPollIntervalSeconds || seconds > TelemetrySchema.MaximumPollIntervalSeconds)
        {
            return $"Poll interval must be from {TelemetrySchema.MinimumPollIntervalSeconds} to {TelemetrySchema.MaximumPollIntervalSeconds} seconds.";
        }

        config.PollIntervalSeconds = seconds;
        if (config.StaleAfterSeconds < seconds)
        {
            config.StaleAfterSeconds = seconds;
        }

        return "";
    }

    /// <summary>Closing the window keeps the collector running. Exit is the explicit stop.</summary>
    public static bool CancelClose(bool exitRequested)
    {
        return !exitRequested;
    }
}
