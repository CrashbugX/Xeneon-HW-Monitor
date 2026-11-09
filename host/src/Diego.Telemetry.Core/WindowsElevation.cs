// The manifest asks Windows to elevate before the process starts.
// This check is the backup for a copy whose manifest was removed.
namespace Diego.Telemetry;

public static class WindowsElevation
{
    public const string DeniedMessage =
        "Xeneon HW Monitor must run as administrator. LibreHardwareMonitor and PawnIO cannot read the sensors otherwise. " +
        "Windows should show a consent prompt because the program asks to run as administrator. " +
        "Close this window and open Xeneon HW Monitor from the Start menu.";

    /// <summary>Refuse only a Windows process that is not elevated. Linux tests are not Windows.</summary>
    public static bool ShouldRefuseStart(bool isWindows, bool isElevated)
    {
        return isWindows && !isElevated;
    }
}
