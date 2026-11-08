// Commands for a narrow Private-profile firewall exception. Nothing here runs netsh.
namespace Diego.Telemetry;

/// <summary>
/// The Pi sends a UDP query to port 8764 and then uses TCP for pairing and
/// telemetry. Windows Firewall drops inbound packets on a Private network
/// unless a rule allows them. The reply to a discovery query is sent back to
/// the Pi's ephemeral port, so the Pi does not need an inbound rule. The host
/// does: inbound UDP on the discovery port, and inbound TCP on the publish
/// port. Both are limited to the Private profile and to the local subnet.
/// Public and Domain profiles are not mentioned. The firewall is not turned off.
/// Changing the publish port does not edit an existing rule. The repair script
/// deletes the named rules and adds them again for the ports in host.json.
/// </summary>
public static class PrivateFirewallPlan
{
    public const string TcpRuleName = "Xeneon HW Monitor Host TCP";

    public const string UdpRuleName = "Xeneon HW Monitor Host UDP";

    public const string LegacyRuleName = "Xeneon HW Monitor Host";

    public static IReadOnlyList<string> Commands(int publishPort, int discoveryPort)
    {
        if (!EndpointRules.TryValidatePort(publishPort, out var publishError))
        {
            throw new ConfigException(publishError);
        }

        if (!EndpointRules.TryValidatePort(discoveryPort, out var discoveryError))
        {
            throw new ConfigException(discoveryError);
        }

        return
        [
            Delete(LegacyRuleName),
            Delete(TcpRuleName),
            Delete(UdpRuleName),
            Add(TcpRuleName, "TCP", publishPort),
            Add(UdpRuleName, "UDP", discoveryPort)
        ];
    }

    public static string RepairCommand(string scriptPath)
    {
        return "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
    }

    private static string Delete(string name)
    {
        return "netsh advfirewall firewall delete rule name=\"" + name + "\"";
    }

    private static string Add(string name, string protocol, int port)
    {
        return "netsh advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow protocol=" + protocol
            + " localport=" + port + " profile=private remoteip=localsubnet";
    }
}
