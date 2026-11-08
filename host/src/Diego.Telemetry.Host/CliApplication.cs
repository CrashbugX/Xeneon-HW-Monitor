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
