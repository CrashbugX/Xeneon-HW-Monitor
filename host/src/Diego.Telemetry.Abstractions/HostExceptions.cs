// Exit-code exceptions: config, hardware open, and real hardware unavailable.
namespace Diego.Telemetry;

public sealed class ConfigException : Exception
{
    public ConfigException(string message)
        : base(message)
    {
    }
}

public sealed class HardwareAccessException : Exception
{
    public HardwareAccessException(string message)
        : base(message)
    {
    }
}

public sealed class RealHardwareUnavailableException : Exception
{
    public RealHardwareUnavailableException(string message)
        : base(message)
    {
    }
}
