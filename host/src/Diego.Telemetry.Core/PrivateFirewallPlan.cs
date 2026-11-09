// Commands for the host firewall exceptions. Nothing here runs netsh.
namespace Diego.Telemetry;

/// <summary>
/// Inbound TCP is pairing and telemetry. Inbound UDP is discovery.
/// The rules name the port, point at this program, and apply to Private and Domain.
/// Public is not opened. The remote address stays on the local subnet.
/// The firewall itself is not turned off. Reinstall deletes these names and adds them again.
/// </summary>
public static class PrivateFirewallPlan
{
    public const string LegacyRuleName = "Xeneon HW Monitor Host";

    public const string LegacyTcpRuleName = "Xeneon HW Monitor Host TCP";

    public const string LegacyUdpRuleName = "Xeneon HW Monitor Host UDP";

    public static string TcpRuleName(int port)
    {
        return "Xeneon HW Monitor - TCP " + port;
    }

    public static string UdpRuleName(int port)
    {
        return "Xeneon HW Monitor - UDP " + port;
    }

    public static IReadOnlyList<FirewallCommand> Commands(int publishPort, int discoveryPort, string programPath)
    {
        if (!EndpointRules.TryValidatePort(publishPort, out var publishError))
        {
            throw new ConfigException(publishError);
        }

        if (!EndpointRules.TryValidatePort(discoveryPort, out var discoveryError))
        {
            throw new ConfigException(discoveryError);
        }

        if (string.IsNullOrWhiteSpace(programPath) || programPath.Contains('"'))
        {
            throw new ConfigException("The firewall rule needs the path of XeneonHwMonitor.exe.");
        }

        return
        [
            Delete(LegacyRuleName),
            Delete(LegacyTcpRuleName),
            Delete(LegacyUdpRuleName),
            Delete(TcpRuleName(publishPort)),
            Delete(UdpRuleName(discoveryPort)),
            Add(TcpRuleName(publishPort), "TCP", publishPort, programPath),
            Add(UdpRuleName(discoveryPort), "UDP", discoveryPort, programPath)
        ];
    }

    /// <summary>True when netsh show rule printed a matching rule name. A missing rule is not an error.</summary>
    public static bool RuleListed(string netshOutput, string ruleName)
    {
        return netshOutput.Contains("Rule Name:", StringComparison.OrdinalIgnoreCase)
            && netshOutput.Contains(ruleName, StringComparison.OrdinalIgnoreCase);
    }

    private static FirewallCommand Delete(string name)
    {
        return new FirewallCommand("advfirewall firewall delete rule name=\"" + name + "\"");
    }

    private static FirewallCommand Add(string name, string protocol, int port, string programPath)
    {
        return new FirewallCommand(
            "advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow protocol=" + protocol
            + " localport=" + port + " profile=private,domain remoteip=localsubnet program=\"" + programPath + "\"");
    }
}

/// <summary>One netsh invocation. FileName is netsh. Arguments are not passed through a shell.</summary>
public readonly record struct FirewallCommand(string Arguments)
{
    public string FileName => "netsh";

    public override string ToString()
    {
        return FileName + " " + Arguments;
    }
}
