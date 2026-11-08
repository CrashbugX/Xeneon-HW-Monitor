// Command dispatch for list, snapshot, watch, check-backend, serve, and the Windows service entry.
using System.Text;
using System.Text.Json;

namespace Diego.Telemetry.Host;

public interface ITextOutput
{
    TextWriter Out { get; }

    TextWriter Error { get; }
}

public sealed class ConsoleTextOutput : ITextOutput
{
    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;
}

public static class CliApplication
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<int> RunAsync(
        string[] args,
        ITextOutput output,
        Func<HostConfig, bool, bool, IHardwareSource> sourceFactory,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ParsedArgs parsed;
        try
        {
            parsed = CommandLine.Parse(args);
        }
        catch (ConfigException ex)
        {
            WriteError(output, "bad_arguments", ex.Message);
            return 2;
        }

        if (parsed.Command is null)
        {
            output.Out.WriteLine(CommandLine.Help());
            return 2;
        }

        if (parsed.Command == "help")
        {
            output.Out.WriteLine(CommandLine.Help());
            return 0;
        }

        try
        {
            var config = ConfigLoader.Load(parsed.ConfigPath);
            if (parsed.IntervalSeconds is not null)
            {
                config.PollIntervalSeconds = parsed.IntervalSeconds.Value;
            }

            if (parsed.MotherboardPolling is not null)
            {
                config.MotherboardPolling = parsed.MotherboardPolling.Value;
            }

            if (parsed.AcceptUniqueCandidates)
            {
                config.AcceptUniqueCandidates = true;
            }

            ConfigLoader.Validate(config);

            if (parsed.Command == "check-backend")
            {
                return CheckBackend(parsed, output);
            }

            if (parsed.Command == "service")
            {
                return RunService(parsed, config, sourceFactory, clock, output, cancellationToken);
            }

            if (parsed.Command == "serve")
            {
                var feed = sourceFactory(config, parsed.Synthetic, parsed.SkipWarmup);
                try
                {
                    return await ServeAsync(parsed, config, feed, clock, output, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await feed.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            if (parsed.Command == "watch" && parsed.Count is null && output.Out == Console.Out && parsed.OutputPath is null && parsed.LogPath is null)
            {
                output.Error.WriteLine("watch with no --count runs until Ctrl+C.");
            }

            var source = sourceFactory(config, parsed.Synthetic, parsed.SkipWarmup);
            if (parsed.Command == "watch")
            {
                return await WatchAsync(parsed, config, source, output, clock, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var observation = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
                var document = SnapshotBuilder.Build(observation, config, clock.UtcNow, sampledOnce: true, sequence: null);
                var json = JsonFormat.Serialize(document, indented: true);
                output.Out.WriteLine(json);
                if (parsed.OutputPath is not null)
                {
                    WriteFile(parsed.OutputPath, json, append: false);
                }

                return 0;
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            output.Error.WriteLine("Cancelled.");
            return 0;
        }
        catch (ConfigException ex)
        {
            WriteError(output, "bad_config", ex.Message);
            return 2;
        }
        catch (RealHardwareUnavailableException ex)
        {
            WriteError(output, "real_hardware_unavailable", ex.Message);
            return 3;
        }
        catch (HardwareAccessException ex)
        {
            WriteError(output, "hardware_open_failed", ex.Message);
            return 4;
        }
    }

    private static async Task<int> ServeAsync(
        ParsedArgs parsed,
        HostConfig config,
        IHardwareSource source,
        IClock clock,
        ITextOutput output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsed.ListenAddress))
        {
            throw new ConfigException("serve requires --listen. There is no default public listener.");
        }

        var token = TokenFile.Read(parsed.TokenFile ?? "", out var warning);
        if (warning is not null)
        {
            output.Error.WriteLine(warning);
        }

        var server = TelemetryServer.Start(
            parsed.ListenAddress,
            parsed.Port ?? 8765,
            token,
            parsed.AllowLan,
            source,
            config,
            clock);
        output.Error.WriteLine($"Telemetry feed is listening on {server.Prefix} GET /v1/telemetry. Cleartext. Token is required and was not printed.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            output.Error.WriteLine("Serve cancelled. Hardware close was requested.");
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// SCM entry. On Linux this returns exit 3 before any native call.
    /// Stopping the service does not run the Pi coordinator; only preshutdown does.
    /// </summary>
    private static int RunService(
        ParsedArgs parsed,
        HostConfig config,
        Func<HostConfig, bool, bool, IHardwareSource> sourceFactory,
        IClock clock,
        ITextOutput output,
        CancellationToken cancellationToken)
    {
        var shutdown = LoadShutdown(parsed.ShutdownConfigPath);
        return WindowsServiceHost.Run(
            async token =>
            {
                if (string.IsNullOrWhiteSpace(parsed.ListenAddress))
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return;
                }

                var tokenText = TokenFile.Read(parsed.TokenFile ?? "", out var warning);
                if (warning is not null)
                {
                    output.Error.WriteLine(warning);
                }

                var source = sourceFactory(config, parsed.Synthetic, parsed.SkipWarmup);
                await using var server = TelemetryServer.Start(
                    parsed.ListenAddress,
                    parsed.Port ?? 8765,
                    tokenText,
                    parsed.AllowLan,
                    source,
                    config,
                    clock);
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            },
            (progress, token) =>
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
                var channel = new HttpPiShutdownChannel(
                    shutdown.PiBaseUrl ?? "http://127.0.0.1:9",
                    shutdown.Token,
                    TimeSpan.FromSeconds(shutdown.RequestTimeoutSeconds));
                var coordinator = new ShutdownCoordinator(
                    shutdown.Policy,
                    channel,
                    new SystemShutdownClock(),
                    shutdown.Configured);
                return coordinator.RunAsync(progress, linked.Token);
            });
    }

    private static LoadedShutdown LoadShutdown(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LoadedShutdown(false, null, "", new ShutdownPolicy(), ShutdownLimits.DefaultRequestTimeoutSeconds);
        }

        var json = File.ReadAllText(path);
        var file = System.Text.Json.JsonSerializer.Deserialize<ShutdownFile>(json, JsonFormat.Config)
            ?? throw new ConfigException("Shutdown config was empty.");
        var policy = new ShutdownPolicy
        {
            Grace = TimeSpan.FromSeconds(file.GraceSeconds),
            AbsentBound = TimeSpan.FromSeconds(file.AbsentBoundSeconds),
            SettleAfterHaltIssued = TimeSpan.FromSeconds(file.SettleAfterHaltIssuedSeconds),
            PollInterval = TimeSpan.FromSeconds(file.PollSeconds)
        };
        ShutdownPolicy.Validate(policy);
        var configured = !string.IsNullOrWhiteSpace(file.PiBaseUrl);
        var token = configured ? TokenFile.Read(file.ShutdownTokenFile ?? "", out _) : "";
        return new LoadedShutdown(configured, file.PiBaseUrl, token, policy, file.RequestTimeoutSeconds);
    }

    private sealed record LoadedShutdown(bool Configured, string? PiBaseUrl, string Token, ShutdownPolicy Policy, int RequestTimeoutSeconds);

    private sealed class ShutdownFile
    {
        public string? PiBaseUrl { get; set; }

        public string? ShutdownTokenFile { get; set; }

        public int GraceSeconds { get; set; } = ShutdownLimits.DefaultGraceSeconds;

        public int AbsentBoundSeconds { get; set; } = ShutdownLimits.DefaultAbsentBoundSeconds;

        public int SettleAfterHaltIssuedSeconds { get; set; } = ShutdownLimits.DefaultSettleSeconds;

        public int PollSeconds { get; set; } = ShutdownLimits.DefaultPollSeconds;

        public int RequestTimeoutSeconds { get; set; } = ShutdownLimits.DefaultRequestTimeoutSeconds;
    }

    private static int CheckBackend(ParsedArgs parsed, ITextOutput output)
    {
        var included = false;
        PawnIoReport? live = null;
#if WINDOWS_LHM
        included = true;
        if (!parsed.Synthetic)
        {
            live = Diego.Telemetry.LibreHardwareMonitor.PawnIoProbe.Read();
        }
#endif
        if (!parsed.Synthetic && !included)
        {
            throw new RealHardwareUnavailableException(
                "This build does not include LibreHardwareMonitorLib. Publish with host/scripts/publish-win-x64.ps1 and run check-backend on Windows. --synthetic is the fixture and does not see PawnIO.");
        }

        var body = new
        {
            schemaVersion = TelemetrySchema.Version,
            command = "check-backend",
            synthetic = parsed.Synthetic,
            backendIncluded = included,
            computerOpenCalled = false,
            pawnIo = parsed.Synthetic
                ? new PawnIoReport
                {
                    Checked = false,
                    Installed = null,
                    DisplayVersion = null,
                    Detection = "Not applicable. --synthetic does not read the PawnIO registry key."
                }
                : live,
            message = parsed.Synthetic
                ? "Synthetic check. No driver was queried and no sensor was opened."
                : "PawnIO presence was read from the registry uninstall key. Computer.Open was not called. Nothing was installed."
        };
        output.Out.WriteLine(JsonFormat.Serialize(body, indented: true));
        if (parsed.OutputPath is not null)
        {
            WriteFile(parsed.OutputPath, JsonFormat.Serialize(body, indented: true), append: false);
        }

        return 0;
    }

    private static async Task<int> WatchAsync(
        ParsedArgs parsed,
        HostConfig config,
        IHardwareSource source,
        ITextOutput output,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var session = new WatchSession();
        var stop = await session.RunAsync(
            source,
            config,
            parsed.Count,
            (document, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var line = JsonFormat.Serialize(document, indented: false);
                output.Out.WriteLine(line);
                if (parsed.OutputPath is not null)
                {
                    WriteFile(parsed.OutputPath, line + Environment.NewLine, append: true);
                }

                if (parsed.LogPath is not null)
                {
                    WriteFile(parsed.LogPath, line + Environment.NewLine, append: true);
                }

                return Task.CompletedTask;
            },
            clock,
            cancellationToken).ConfigureAwait(false);
        if (stop == WatchStop.Cancelled)
        {
            output.Error.WriteLine("Watch cancelled. Hardware close was requested.");
        }

        return 0;
    }

    private static void WriteFile(string path, string contents, bool append)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (append)
        {
            File.AppendAllText(path, contents, Utf8);
        }
        else
        {
            File.WriteAllText(path, contents, Utf8);
        }
    }

    private static void WriteError(ITextOutput output, string code, string message)
    {
        output.Error.WriteLine(message);
        var body = new
        {
            schemaVersion = TelemetrySchema.Version,
            ok = false,
            error = new { code, message }
        };
        output.Out.WriteLine(JsonSerializer.Serialize(body, JsonFormat.Pretty));
    }
}
