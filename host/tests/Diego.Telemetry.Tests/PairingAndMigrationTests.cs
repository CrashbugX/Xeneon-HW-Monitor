using System.Net;
using System.Net.Sockets;
using System.Text;
using Diego.Telemetry.Fake;
using Diego.Telemetry.Host;

namespace Diego.Telemetry.Tests;

public sealed class PairingAndMigrationTests
{
    [Fact]
    public void PairingRequiresApprovalAndDoesNotStoreTheRawToken()
    {
        var clock = new MutableClock();
        var broker = new PairingBroker(clock);
        var begun = broker.Begin("10.0.0.8", "telemetry-pi");
        Assert.True(begun.Ok);
        Assert.Null(begun.ErrorCode);
        var pending = Assert.Single(broker.Pending);
        Assert.Equal(6, pending.Code.Length);
        Assert.False(pending.Approved);

        var early = broker.Complete(begun.PairingId!, pending.Code, "10.0.0.8");
        Assert.False(early.Ok);
        Assert.Equal("awaiting_approval", early.ErrorCode);
        Assert.Null(early.Token);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var wrong = broker.Complete(begun.PairingId!, "000000", "10.0.0.8");
            Assert.Equal("bad_code", wrong.ErrorCode);
        }

        var locked = broker.Complete(begun.PairingId!, "000000", "10.0.0.8");
        Assert.Equal("locked", locked.ErrorCode);
        Assert.Empty(broker.Pending);

        begun = broker.Begin("10.0.0.8", "telemetry-pi");
        pending = Assert.Single(broker.Pending);
        Assert.Null(broker.Approve(begun.PairingId!));
        var done = broker.Complete(begun.PairingId!, pending.Code, "10.0.0.8");
        Assert.True(done.Ok);
        Assert.NotNull(done.Token);
        Assert.True(broker.Authorize(done.Token!));
        Assert.False(broker.Authorize(done.Token + "x"));

        var directory = Directory.CreateTempSubdirectory("xeneon-pair");
        var path = Path.Combine(directory.FullName, "devices.json");
        broker.Save(path);
        var stored = File.ReadAllText(path);
        Assert.DoesNotContain(done.Token, stored, StringComparison.Ordinal);
        Assert.Contains(PairingBroker.Hash(done.Token!), stored, StringComparison.Ordinal);

        var loaded = new PairingBroker(clock);
        loaded.Load(path);
        Assert.True(loaded.Authorize(done.Token!));
        Assert.True(loaded.Revoke(done.DeviceId!));
        Assert.False(loaded.Authorize(done.Token!));
        directory.Delete(recursive: true);
    }

    [Fact]
    public void ExpiredCodeAndBeginRateLimitRefuseNewSessions()
    {
        var clock = new MutableClock();
        var broker = new PairingBroker(clock);
        var begun = broker.Begin("10.0.0.9", "pi");
        clock.UtcNow = clock.UtcNow.AddMinutes(3);
        Assert.Empty(broker.Pending);
        var expired = broker.Complete(begun.PairingId!, "123456", "10.0.0.9");
        Assert.Equal("not_found", expired.ErrorCode);

        clock.UtcNow = DateTimeOffset.Parse("2026-11-08T22:12:00Z");
        for (var i = 0; i < PairingBroker.MaxBeginsPerWindow; i++)
        {
            Assert.True(broker.Begin("10.0.0.9", "pi").Ok);
        }

        var limited = broker.Begin("10.0.0.9", "pi");
        Assert.False(limited.Ok);
        Assert.Equal("rate_limited", limited.ErrorCode);
    }

    [Fact]
    public void DiscoveryAnnouncementHasNoToken()
    {
        var bytes = DiscoveryMessage.Announce(new DiscoveryAnnouncement
        {
            Name = "DESKTOP",
            TelemetryPort = 8765,
            Product = ProductInfo.Name,
            Version = ProductInfo.Version
        });
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(DiscoveryMessage.TryParse(bytes, out var packet));
        Assert.Equal("announce", packet.Op);
        Assert.Equal(8765, packet.Announcement!.TelemetryPort);
        Assert.True(DiscoveryMessage.TryParse(DiscoveryMessage.Query(), out var query));
        Assert.Equal("query", query.Op);
    }

    [Fact]
    public void MigrationDropsTokenFieldsAndKeepsRoles()
    {
        var directory = Directory.CreateTempSubdirectory("xeneon-migrate");
        var path = Path.Combine(directory.FullName, "host.json");
        File.WriteAllText(path, """
        {
          "pollIntervalSeconds": 2,
          "motherboardPolling": false,
          "token": "do-not-keep-this",
          "roles": { "cpuTemperature": "/amdcpu/0/temperature/2", "fanCpu": [], "fanGpu": [], "fanCase": [], "cpuUtilizationLogical": [] }
        }
        """);
        var result = ConfigMigration.MigrateFile(path);
        Assert.True(result.Changed);
        Assert.True(File.Exists(path + ".bak"));
        var written = File.ReadAllText(path);
        Assert.DoesNotContain("do-not-keep-this", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", written, StringComparison.OrdinalIgnoreCase);
        var loaded = ConfigLoader.Parse(written);
        Assert.Equal("0.2", loaded.ConfigSchema);
        Assert.True(loaded.AutoMap);
        Assert.Equal(2d, loaded.PollIntervalSeconds);
        Assert.False(loaded.MotherboardPolling);
        Assert.Equal("/amdcpu/0/temperature/2", loaded.Roles.CpuTemperature);
        Assert.Equal("current", ConfigMigration.MigrateFile(path).Detail);
        directory.Delete(recursive: true);
    }

    [Fact]
    public async Task PortChangeRejectsPrivilegedAndMovesTheListener()
    {
        Assert.False(EndpointRules.TryValidatePort(80, out var privileged));
        Assert.Contains("1024", privileged, StringComparison.Ordinal);
        Assert.False(EndpointRules.TryValidatePort(0, out _));
        var config = new HostConfig();
        Assert.Equal("", HostSettings.ApplyPort(config, 8771));
        Assert.Equal(8771, config.PublishPort);
        Assert.NotEqual("", HostSettings.ApplyPort(config, 80));
        Assert.Equal(8771, config.PublishPort);
        var port = FreePort();
        var server = TelemetryServer.Start("127.0.0.1", port, "legacy-token", false, new SyntheticHardwareSource(), config, new MutableClock());
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var discover = await http.GetStringAsync($"http://127.0.0.1:{port}/v1/discover");
            Assert.DoesNotContain("token", discover, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ProductInfo.Name, discover, StringComparison.Ordinal);
            Assert.Contains(port.ToString(), discover, StringComparison.Ordinal);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task LoopbackPairingThenTelemetryAndRevoke()
    {
        var clock = new MutableClock();
        var broker = new PairingBroker(clock);
        var port = FreePort();
        var server = TelemetryServer.Start("127.0.0.1", port, "legacy-token", false, new SyntheticHardwareSource(), new HostConfig { PollIntervalSeconds = 0.5 }, clock, broker);
        using var http = new HttpClient();
        var root = $"http://127.0.0.1:{port}";
        try
        {
            var begin = await http.PostAsync(root + "/v1/pair/begin", Json("""{"displayName":"telemetry-pi"}"""));
            var beginBody = await begin.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            Assert.DoesNotContain("code", beginBody, StringComparison.OrdinalIgnoreCase);
            using var begun = System.Text.Json.JsonDocument.Parse(beginBody);
            var id = begun.RootElement.GetProperty("pairingId").GetString()!;
            var pending = await http.GetStringAsync(root + "/v1/pair/pending");
            using var pendingJson = System.Text.Json.JsonDocument.Parse(pending);
            var code = pendingJson.RootElement.GetProperty("pending")[0].GetProperty("code").GetString()!;
            var tooSoon = await http.PostAsync(root + "/v1/pair/complete", Json($$"""{"pairingId":"{{id}}","code":"{{code}}"}"""));
            Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
            var approved = await http.PostAsync(root + "/v1/pair/approve", Json($$"""{"pairingId":"{{id}}"}"""));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var done = await http.PostAsync(root + "/v1/pair/complete", Json($$"""{"pairingId":"{{id}}","code":"{{code}}"}"""));
            var doneBody = await done.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
            using var paired = System.Text.Json.JsonDocument.Parse(doneBody);
            var token = paired.RootElement.GetProperty("deviceToken").GetString()!;
            var deviceId = paired.RootElement.GetProperty("deviceId").GetString()!;
            HttpResponseMessage sample = new(HttpStatusCode.ServiceUnavailable);
            for (var i = 0; i < 20; i++)
            {
                sample.Dispose();
                using var request = new HttpRequestMessage(HttpMethod.Get, root + "/v1/telemetry");
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                sample = await http.SendAsync(request);
                if (sample.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
            var inventory = new HttpRequestMessage(HttpMethod.Get, root + "/v1/inventory");
            inventory.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            var inventoryResponse = await http.SendAsync(inventory);
            Assert.Equal(HttpStatusCode.OK, inventoryResponse.StatusCode);
            var revoked = await http.PostAsync(root + "/v1/pair/revoke", Json($$"""{"deviceId":"{{deviceId}}"}"""));
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            using var after = new HttpRequestMessage(HttpMethod.Get, root + "/v1/telemetry");
            after.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            var refused = await http.SendAsync(after);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            sample.Dispose();
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public void FirewallCommandsFollowTheConfiguredPortsAndStayOnThePrivateSubnet()
    {
        var commands = PrivateFirewallPlan.Commands(8771, 8764);
        var text = string.Join("\n", commands);
        Assert.Contains("protocol=TCP", text, StringComparison.Ordinal);
        Assert.Contains("localport=8771", text, StringComparison.Ordinal);
        Assert.Contains("protocol=UDP", text, StringComparison.Ordinal);
        Assert.Contains("localport=8764", text, StringComparison.Ordinal);
        Assert.Contains("profile=private", text, StringComparison.Ordinal);
        Assert.Contains("remoteip=localsubnet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("profile=public", text, StringComparison.Ordinal);
        Assert.DoesNotContain("localport=8765", text, StringComparison.Ordinal);
        Assert.Contains("delete rule name=\"Xeneon HW Monitor Host\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondCollectorDoesNotTakeTheLock()
    {
        var path = Path.Combine(Path.GetTempPath(), "xeneon-lock-" + Guid.NewGuid().ToString("N"));
        using var first = CollectorOwner.TryAcquire(path);
        using var second = CollectorOwner.TryAcquire(path);
        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.True(HostSettings.CancelClose(exitRequested: false));
        Assert.False(HostSettings.CancelClose(exitRequested: true));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static StringContent Json(string body)
    {
        return new StringContent(body, Encoding.UTF8, "application/json");
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-11-08T22:00:00Z");
    }
}
