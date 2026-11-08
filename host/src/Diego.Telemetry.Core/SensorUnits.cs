// Converts LibreHardwareMonitor 0.9.6 units. Data is 2^30 bytes, SmallData is 2^20 bytes.
namespace Diego.Telemetry;

/// <summary>
/// Converts LibreHardwareMonitorLib 0.9.6 sensor numbers into explicit units.
/// Data is binary gigabytes (2^30 bytes) and SmallData is binary megabytes (2^20 bytes),
/// as commented on <c>SensorType</c> in that tag. Decimal GB/MB are not used.
/// </summary>
public static class SensorUnits
{
    public const double BinaryMebibyte = 1024d * 1024d;

    public const double BinaryGibibyte = 1024d * 1024d * 1024d;

    public static UnitConversion Convert(string sensorType, double? libraryValue)
    {
        var (libraryUnit, normalizedUnit, factor) = sensorType switch
        {
            "Temperature" => ("celsius", "celsius", 1d),
            "Load" or "Control" or "Level" or "Humidity" => ("percent", "percent", 1d),
            "Fan" => ("rpm", "rpm", 1d),
            "Data" => ("binary_gibibyte", "bytes", BinaryGibibyte),
            "SmallData" => ("binary_mebibyte", "bytes", BinaryMebibyte),
            "Clock" => ("megahertz", "megahertz", 1d),
            "Voltage" => ("volt", "volt", 1d),
            "Current" => ("ampere", "ampere", 1d),
            "Power" => ("watt", "watt", 1d),
            "Frequency" => ("hertz", "hertz", 1d),
            "Flow" => ("liters_per_hour", "liters_per_hour", 1d),
            "Throughput" => ("bytes_per_second", "bytes_per_second", 1d),
            "Factor" => ("factor", "factor", 1d),
            "TimeSpan" => ("seconds", "seconds", 1d),
            "Timing" => ("nanoseconds", "nanoseconds", 1d),
            "Energy" => ("milliwatt_hour", "milliwatt_hour", 1d),
            "Noise" => ("dba", "dba", 1d),
            "Conductivity" => ("microsiemens_per_centimeter", "microsiemens_per_centimeter", 1d),
            _ => ("unknown", "unknown", 1d)
        };

        double? normalized = libraryValue is null ? null : libraryValue.Value * factor;
        if (normalized is not null && normalizedUnit == "bytes")
        {
            normalized = Math.Round(normalized.Value, MidpointRounding.AwayFromZero);
        }

        return new UnitConversion(libraryUnit, normalizedUnit, normalized);
    }
}
