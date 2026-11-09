// The logon task is what starts the host after sign-in without a consent prompt.
// A Run key would start it unelevated, and the manifest would then ask every time.
using System.Text;

namespace Diego.Telemetry;

public static class LogonTaskPlan
{
    public const string TaskName = "Xeneon HW Monitor Host";

    /// <summary>Thirty seconds after the desktop is ready, so the shell and the network exist.</summary>
    public const string Delay = "PT30S";

    /// <summary>
    /// Task Scheduler XML. HighestAvailable plus an interactive token stores no password.
    /// ExecutionTimeLimit PT0S means the task is not stopped after three days.
    /// The command is only the host executable and the fixed argument --autostart.
    /// </summary>
    public static string Xml(string userId, string exePath)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.IndexOfAny(['<', '>', '&']) >= 0)
        {
            throw new ConfigException("The Windows user name for the logon task is empty or has XML characters.");
        }

        if (string.IsNullOrWhiteSpace(exePath) || exePath.IndexOfAny(['<', '>', '&', '"']) >= 0)
        {
            throw new ConfigException("The host path for the logon task is empty or has XML characters.");
        }

        var user = Escape(userId);
        var exe = Escape(exePath);
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        xml.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        xml.AppendLine("  <RegistrationInfo>");
        xml.AppendLine("    <Description>Start Xeneon HW Monitor at logon, already elevated.</Description>");
        xml.AppendLine("  </RegistrationInfo>");
        xml.AppendLine("  <Triggers>");
        xml.AppendLine("    <LogonTrigger>");
        xml.AppendLine("      <Enabled>true</Enabled>");
        xml.AppendLine("      <UserId>" + user + "</UserId>");
        xml.AppendLine("      <Delay>" + Delay + "</Delay>");
        xml.AppendLine("    </LogonTrigger>");
        xml.AppendLine("  </Triggers>");
        xml.AppendLine("  <Principals>");
        xml.AppendLine("    <Principal id=\"Author\">");
        xml.AppendLine("      <UserId>" + user + "</UserId>");
        xml.AppendLine("      <LogonType>InteractiveToken</LogonType>");
        xml.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
        xml.AppendLine("    </Principal>");
        xml.AppendLine("  </Principals>");
        xml.AppendLine("  <Settings>");
        xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        xml.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        xml.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
        xml.AppendLine("    <Enabled>true</Enabled>");
        xml.AppendLine("  </Settings>");
        xml.AppendLine("  <Actions Context=\"Author\">");
        xml.AppendLine("    <Exec>");
        xml.AppendLine("      <Command>" + exe + "</Command>");
        xml.AppendLine("      <Arguments>--autostart</Arguments>");
        xml.AppendLine("    </Exec>");
        xml.AppendLine("  </Actions>");
        xml.AppendLine("</Task>");
        return xml.ToString();
    }

    public static string CreateArguments(string xmlPath)
    {
        return "/Create /F /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\"";
    }

    public static string DeleteArguments()
    {
        return "/Delete /F /TN \"" + TaskName + "\"";
    }

    public static string QueryArguments()
    {
        return "/Query /TN \"" + TaskName + "\"";
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
