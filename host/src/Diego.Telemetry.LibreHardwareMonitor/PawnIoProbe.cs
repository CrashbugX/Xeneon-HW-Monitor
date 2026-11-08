using LibreHardwareMonitor.PawnIo;

namespace Diego.Telemetry.LibreHardwareMonitor;

public static class PawnIoProbe
{
    public const string DetectionText =
        "LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled reads HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO DisplayVersion, including the 64-bit registry view. The device path used later by the library is \\\\?\\GLOBALROOT\\Device\\PawnIO. This check does not open that device and does not run an installer.";

    public static PawnIoReport Read()
    {
        var version = PawnIo.Version;
        return new PawnIoReport
        {
            Checked = true,
            Installed = PawnIo.IsInstalled,
            DisplayVersion = version?.ToString(),
            Detection = DetectionText
        };
    }
}
