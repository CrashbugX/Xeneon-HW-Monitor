namespace Diego.Telemetry;

public static class TelemetrySchema
{
    public const string Version = "1.0.0";

    public const double MinimumPollIntervalSeconds = 0.5;

    public const double MaximumPollIntervalSeconds = 3600;

    public const double MaximumStaleAfterSeconds = 86400;
}
