// Windows-only actions for the elevated host: the logon task, the firewall
// rules, and launching a verified installer. Nothing here runs on Linux tests.
using System.Diagnostics;
using System.Net.Http;
using System.Security.Principal;
using System.Text;

namespace Diego.Telemetry.Desktop;

public static class WindowsHostOps
{
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string InstallLogonTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "The logon task is created on Windows. Nothing was changed here.";
        }

        var exe = Environment.ProcessPath ?? "";
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        var xml = LogonTaskPlan.Xml(user, exe);
        var path = Path.Combine(Path.GetTempPath(), "xeneon-logon-task.xml");
        File.WriteAllText(path, xml, Encoding.Unicode);
        var code = Run("schtasks.exe", LogonTaskPlan.CreateArguments(path), out _);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }

        return code == 0
            ? "Start with Windows is on. The host will open hidden in the tray at the next logon."
            : "The logon task was not created. schtasks exited " + code + ".";
    }

    public static string RemoveLogonTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "The logon task is removed on Windows. Nothing was changed here.";
        }

        var code = Run("schtasks.exe", LogonTaskPlan.DeleteArguments(), out _);
        return code == 0
            ? "Start with Windows is off."
            : "No logon task was removed. schtasks exited " + code + ".";
    }

    public static bool LogonTaskExists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return Run("schtasks.exe", LogonTaskPlan.QueryArguments(), out _) == 0;
    }

    /// <summary>Replace this app's rules. A missing old rule is not a failure. The firewall is not turned off.</summary>
    public static string ApplyFirewall(int publishPort, int discoveryPort)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Fix firewall runs on Windows, where this program is already elevated.";
        }

        var program = Environment.ProcessPath ?? "";
        string? failure = null;
        foreach (var command in PrivateFirewallPlan.Commands(publishPort, discoveryPort, program))
        {
            var code = Run("netsh", command.Arguments, out var output);
            var adding = command.Arguments.Contains(" add rule ", StringComparison.Ordinal);
            if (adding && code != 0)
            {
                failure = "netsh could not add a rule. " + output.Trim();
            }
        }

        return failure ?? ("Firewall rules are in place for TCP " + publishPort + " and UDP " + discoveryPort + ". Public stays closed.");
    }

    public static bool RulesPresent(int publishPort, int discoveryPort)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return Listed(PrivateFirewallPlan.TcpRuleName(publishPort))
            && Listed(PrivateFirewallPlan.UdpRuleName(discoveryPort));
    }

    /// <summary>
    /// Download a release installer, check its size and SHA-256, then start it with /S.
    /// Returns an empty string when the installer was started. Does not wait for it.
    /// </summary>
    public static async Task<string> DownloadAndLaunchAsync(UpdateOffer offer)
    {
        if (!HostUpdate.IsAllowedDownloadUrl(offer.DownloadUrl))
        {
            return "That download address is not a release of " + HostUpdate.Repository + ".";
        }

        if (offer.Sha256.Length != 64 && offer.Sha256Url.Length > 0)
        {
            if (!HostUpdate.IsAllowedDownloadUrl(offer.Sha256Url))
            {
                return "The checksum address is not a release of " + HostUpdate.Repository + ".";
            }

            offer.Sha256 = HostUpdate.TryReadSha256File(await GetTextAsync(offer.Sha256Url));
        }

        if (offer.Sha256.Length != 64)
        {
            return "This release did not publish a SHA-256. Update now stays off.";
        }

        var destination = Path.Combine(Path.GetTempPath(), offer.AssetName);
        await DownloadAsync(offer.DownloadUrl, destination);
        var problem = HostUpdate.VerifyFile(destination, offer.Size, offer.Sha256);
        if (problem.Length > 0)
        {
            try
            {
                File.Delete(destination);
            }
            catch (IOException)
            {
            }

            return problem;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = destination,
            Arguments = "/S",
            UseShellExecute = true
        });
        return "";
    }

    public static async Task<UpdateOffer?> CheckAsync()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(HostUpdate.ReleasesUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return HostUpdate.FindNewer(json, ProductInfo.Version);
    }

    private static bool Listed(string ruleName)
    {
        var code = Run("netsh", "advfirewall firewall show rule name=\"" + ruleName + "\"", out var output);
        return code == 0 && PrivateFirewallPlan.RuleListed(output, ruleName);
    }

    private static async Task<string> GetTextAsync(string url)
    {
        using var client = CreateClient();
        return await client.GetStringAsync(url);
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        await using var stream = await client.GetStreamAsync(url);
        await using var file = File.Create(destination);
        await stream.CopyToAsync(file);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XeneonHwMonitor/" + ProductInfo.Version);
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static int Run(string file, string arguments, out string output)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
