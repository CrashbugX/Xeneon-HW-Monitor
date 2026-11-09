using Diego.Telemetry;

namespace Diego.Telemetry.Tests;

public sealed class HostReleaseTests
{
    [Fact]
    public void LogonTaskRunsElevatedAtLogonWithoutATimeLimit()
    {
        var xml = LogonTaskPlan.Xml(@"SOULER\diego", @"C:\Program Files\Xeneon HW Monitor Host\XeneonHwMonitor.exe");
        Assert.Contains("<LogonTrigger>", xml, StringComparison.Ordinal);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml, StringComparison.Ordinal);
        Assert.Contains("<Delay>PT30S</Delay>", xml, StringComparison.Ordinal);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml, StringComparison.Ordinal);
        Assert.Contains("--autostart", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentVersion\\Run", xml, StringComparison.Ordinal);
        Assert.Contains("Xeneon HW Monitor Host", LogonTaskPlan.CreateArguments("task.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnelevatedWindowsStartIsRefused()
    {
        Assert.True(WindowsElevation.ShouldRefuseStart(isWindows: true, isElevated: false));
        Assert.False(WindowsElevation.ShouldRefuseStart(isWindows: true, isElevated: true));
        Assert.False(WindowsElevation.ShouldRefuseStart(isWindows: false, isElevated: false));
        Assert.Contains("administrator", WindowsElevation.DeniedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShutdownRunsOnlyForWindowsShutdown()
    {
        Assert.True(PiShutdownGate.ShouldRun(settingEnabled: true, userExit: false, windowsShutdown: true));
        Assert.False(PiShutdownGate.ShouldRun(settingEnabled: true, userExit: true, windowsShutdown: true));
        Assert.False(PiShutdownGate.ShouldRun(settingEnabled: false, userExit: false, windowsShutdown: true));
        Assert.False(PiShutdownGate.ShouldRun(settingEnabled: true, userExit: false, windowsShutdown: false));
    }

    [Fact]
    public void UpdaterPicksTheHostAssetAndRequiresAChecksum()
    {
        var json = """
        [
          {"tag_name":"v0.9.0","draft":false,"prerelease":false,"body":"client only","assets":[{"name":"client.zip","size":10,"browser_download_url":"https://github.com/CrashbugX/Xeneon-HW-Monitor/releases/download/v0.9.0/client.zip"}]},
          {"tag_name":"host-v0.2.0","draft":false,"prerelease":false,"body":"old","assets":[{"name":"XeneonHwMonitorHost-0.2.0-win-x64.exe","size":20,"browser_download_url":"https://github.com/CrashbugX/Xeneon-HW-Monitor/releases/download/host-v0.2.0/XeneonHwMonitorHost-0.2.0-win-x64.exe"}]},
          {"tag_name":"host-v0.4.0","draft":false,"prerelease":true,"body":"beta","assets":[{"name":"XeneonHwMonitorHost-0.4.0-win-x64.exe","size":20,"browser_download_url":"https://github.com/CrashbugX/Xeneon-HW-Monitor/releases/download/host-v0.4.0/XeneonHwMonitorHost-0.4.0-win-x64.exe"}]},
          {"tag_name":"host-v0.3.1","draft":false,"prerelease":false,"body":"Notes\nSHA-256: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n","assets":[{"name":"XeneonHwMonitorHost-0.3.1-win-x64.exe","size":30,"browser_download_url":"https://github.com/CrashbugX/Xeneon-HW-Monitor/releases/download/host-v0.3.1/XeneonHwMonitorHost-0.3.1-win-x64.exe"}]}
        ]
        """;
        var offer = HostUpdate.FindNewer(json, "0.3.0");
        Assert.NotNull(offer);
        Assert.Equal("0.3.1", offer!.Version);
        Assert.Equal("Notes", offer.Notes);
        Assert.StartsWith("aaaaaaaa", offer.Sha256, StringComparison.Ordinal);
        Assert.True(offer.HasChecksum);
        Assert.Null(HostUpdate.FindNewer(json, "0.3.1"));
        Assert.False(HostUpdate.IsAllowedDownloadUrl("http://example.com/XeneonHwMonitorHost-0.3.1-win-x64.exe"));
    }

    [Fact]
    public void PairedPiAddressIsStoredWithoutTheTelemetryToken()
    {
        var broker = new PairingBroker(new MutableClock());
        var begun = broker.Begin("10.1.2.3", "telemetry-pi");
        var pending = Assert.Single(broker.Pending);
        Assert.Null(broker.Approve(begun.PairingId!));
        var done = broker.Complete(begun.PairingId!, pending.Code, "10.1.2.3");
        Assert.True(done.Ok);
        Assert.NotNull(done.Token);
        Assert.Empty(broker.ShutdownTargets());
        var directory = Directory.CreateTempSubdirectory("xeneon-peer");
        var path = Path.Combine(directory.FullName, "devices.json");
        broker.Save(path);
        var stored = File.ReadAllText(path);
        Assert.Contains("10.1.2.3", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(done.Token!, stored, StringComparison.Ordinal);
        var error = broker.RememberControl(done.Token!, "10.1.2.3", 8766, new string('b', 32));
        Assert.Null(error);
        var target = Assert.Single(broker.ShutdownTargets());
        Assert.Equal("http://10.1.2.3:8766", target.BaseUrl);
        Assert.Equal(new string('b', 32), target.Token);
        directory.Delete(recursive: true);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-11-08T22:00:00Z");
    }
}
