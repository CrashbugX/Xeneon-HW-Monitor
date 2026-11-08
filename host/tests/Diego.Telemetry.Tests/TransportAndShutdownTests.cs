using System.Net;
using System.Net.Sockets;
using Diego.Telemetry.Fake;
using Diego.Telemetry.Host;

namespace Diego.Telemetry.Tests;

/// <summary>
/// Host feed authentication and the preshutdown coordinator. No Windows service is installed
/// and nothing is powered off.
/// </summary>
public sealed class TransportAndShutdownTests
{
    [Fact]
    public void BindRefusesEveryInterfaceWithoutAllowLan()
    {
        var error = Assert.Throws<ConfigException>(() => TelemetryServer.ValidateBind("0.0.0.0", 8765, allowLan: false));
        Assert.Contains("allow-lan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServeRejectsMissingAndWrongTokensAndQueryTokens()
    {
        var token = "test-token-not-a-secret-from-the-repo";
        var port = FreePort();
        var server = TelemetryServer.Start(
            "127.0.0.1",
            port,
            token,
            allowLan: false,
            new SyntheticHardwareSource(),
            new HostConfig { PollIntervalSeconds = 0.5 },
            new SystemClock());
        using var http = new HttpClient();
        var url = $"http://127.0.0.1:{port}/v1/telemetry";
        try
        {
            var missing = await http.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.DoesNotContain(token, await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var wrong = new HttpRequestMessage(HttpMethod.Get, url);
            wrong.Headers.TryAddWithoutValidation("Authorization", "Bearer nope");
            var refused = await http.SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

            var query = await http.GetAsync(url + "?token=" + token);
            Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
            Assert.DoesNotContain(token, await query.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            HttpResponseMessage accepted = new(HttpStatusCode.ServiceUnavailable);
            for (var i = 0; i < 20; i++)
            {
                accepted.Dispose();
                using var ok = new HttpRequestMessage(HttpMethod.Get, url);
                ok.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                accepted = await http.SendAsync(ok);
                if (accepted.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var body = await accepted.Content.ReadAsStringAsync();
            Assert.Contains("\"schemaVersion\":\"1.0.0\"", body, StringComparison.Ordinal);
            Assert.Contains("\"synthetic\":true", body, StringComparison.Ordinal);
            Assert.DoesNotContain(token, body, StringComparison.Ordinal);
            accepted.Dispose();
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ServeCommandWithoutListenIsRejected()
    {
        var code = await CliApplication.RunAsync(
            ["serve", "--synthetic"],
            new StringText(new StringWriter(), new StringWriter()),
            Program.CreateSource,
            new SystemClock(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task ServiceCommandIsUnavailableOffWindows()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApplication.RunAsync(
            ["service"],
            new StringText(stdout, stderr),
            Program.CreateSource,
            new SystemClock(),
            CancellationToken.None);
        Assert.Equal(3, code);
        Assert.Contains("Windows", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbsentPiDoesNotWaitTheFullGrace()
    {
        var clock = new ManualClock();
        var channel = new ScriptedChannel(_ => new PiShutdownObservation(PiShutdownPhase.Unreachable, "down"));
        var coordinator = new ShutdownCoordinator(
            new ShutdownPolicy { Grace = TimeSpan.FromSeconds(45), AbsentBound = TimeSpan.FromSeconds(5), SettleAfterHaltIssued = TimeSpan.FromSeconds(15), PollInterval = TimeSpan.FromSeconds(1) },
            channel,
            clock,
            piConfigured: true);
        var result = await coordinator.RunAsync(_ => { }, CancellationToken.None);
        Assert.Equal(ShutdownOutcome.AbsentProceeded, result.Outcome);
        Assert.InRange(result.Elapsed.TotalSeconds, 5, 8);
        Assert.True(result.Posts < 15);
    }

    [Fact]
    public async Task UnauthorizedDoesNotBurnTheGracePeriod()
    {
        var clock = new ManualClock();
        var channel = new ScriptedChannel(_ => new PiShutdownObservation(PiShutdownPhase.Unauthorized, "no"));
        var coordinator = new ShutdownCoordinator(ShortPolicy(), channel, clock, piConfigured: true);
        var result = await coordinator.RunAsync(_ => { }, CancellationToken.None);
        Assert.Equal(ShutdownOutcome.AuthRejected, result.Outcome);
        Assert.Equal(1, result.Posts);
        Assert.Equal(0, clock.Elapsed.TotalSeconds);
    }

    [Fact]
    public async Task HaltIssuedWaitsForSettleNotJustTheAck()
    {
        var clock = new ManualClock();
        var posts = 0;
        var channel = new ScriptedChannel(call =>
        {
            if (call == ScriptedCall.Post)
            {
                posts++;
                return new PiShutdownObservation(PiShutdownPhase.Accepted, "accepted");
            }

            return new PiShutdownObservation(posts > 0 && clock.Elapsed.TotalSeconds >= 2
                ? PiShutdownPhase.HaltIssued
                : PiShutdownPhase.Syncing, "status");
        });
        var coordinator = new ShutdownCoordinator(
            new ShutdownPolicy { Grace = TimeSpan.FromSeconds(45), AbsentBound = TimeSpan.FromSeconds(5), SettleAfterHaltIssued = TimeSpan.FromSeconds(15), PollInterval = TimeSpan.FromSeconds(1) },
            channel,
            clock,
            piConfigured: true);
        var result = await coordinator.RunAsync(_ => { }, CancellationToken.None);
        Assert.Equal(ShutdownOutcome.HaltIssuedAndSettled, result.Outcome);
        Assert.True(result.Elapsed.TotalSeconds >= 15);
        Assert.Equal(1, result.Posts);
    }

    [Fact]
    public async Task UnconfiguredPiReturnsImmediately()
    {
        var clock = new ManualClock();
        var channel = new ScriptedChannel(_ => throw new InvalidOperationException("must not be called"));
        var coordinator = new ShutdownCoordinator(ShortPolicy(), channel, clock, piConfigured: false);
        var result = await coordinator.RunAsync(_ => { }, CancellationToken.None);
        Assert.Equal(ShutdownOutcome.NotConfigured, result.Outcome);
        Assert.Equal(0, result.Posts);
    }

    [Fact]
    public void GraceAboveTheCapIsRejected()
    {
        var error = Assert.Throws<ConfigException>(() => ShutdownPolicy.Validate(new ShutdownPolicy { Grace = TimeSpan.FromSeconds(1000) }));
        Assert.Contains("90", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenCompareDoesNotTreatAPrefixAsEqual()
    {
        Assert.False(TokenFile.FixedTimeEquals("abc", "abcd"));
        Assert.True(TokenFile.FixedTimeEquals("same-token", "same-token"));
    }

    private static ShutdownPolicy ShortPolicy()
    {
        return new ShutdownPolicy
        {
            Grace = TimeSpan.FromSeconds(45),
            AbsentBound = TimeSpan.FromSeconds(5),
            SettleAfterHaltIssued = TimeSpan.FromSeconds(15),
            PollInterval = TimeSpan.FromSeconds(1)
        };
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private enum ScriptedCall
    {
        Post,
        Status
    }

    private sealed class ManualClock : IShutdownClock
    {
        public TimeSpan Elapsed { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Elapsed += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedChannel : IPiShutdownChannel
    {
        private readonly Func<ScriptedCall, PiShutdownObservation> _next;

        public ScriptedChannel(Func<ScriptedCall, PiShutdownObservation> next)
        {
            _next = next;
        }

        public Task<PiShutdownObservation> PostPoweroffAsync(string nonce, DateTimeOffset issuedAtUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(_next(ScriptedCall.Post));
        }

        public Task<PiShutdownObservation> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_next(ScriptedCall.Status));
        }
    }

    private sealed class StringText(StringWriter stdout, StringWriter stderr) : ITextOutput
    {
        public TextWriter Out => stdout;

        public TextWriter Error => stderr;
    }
}
