namespace Diego.Telemetry;

/// <summary>Port and URL checks for the publisher. URLs never carry a token.</summary>
public static class EndpointRules
{
    public static bool TryValidatePort(int port, out string error)
    {
        if (port is < 1 or > 65535)
        {
            error = "Port must be from 1 to 65535.";
            return false;
        }

        if (port < 1024)
        {
            error = "Ports below 1024 are reserved for system services. Choose 1024 or higher.";
            return false;
        }

        error = "";
        return true;
    }

    public static string TelemetryUrl(string host, int port)
    {
        return $"http://{host}:{port}/v1/telemetry";
    }

    public static string InventoryUrl(string host, int port)
    {
        return $"http://{host}:{port}/v1/inventory";
    }

    public static string DiscoverUrl(string host, int port)
    {
        return $"http://{host}:{port}/v1/discover";
    }
}
