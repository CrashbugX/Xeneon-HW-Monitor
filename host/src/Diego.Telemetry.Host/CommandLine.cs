// Argument parser. Tokens are file paths, never the secret itself.
namespace Diego.Telemetry.Host;

public sealed class ParsedArgs
{
    public string? Command { get; init; }

    public string? ConfigPath { get; init; }

    public string? OutputPath { get; init; }

    public string? LogPath { get; init; }

    public bool Synthetic { get; init; }

    public bool SkipWarmup { get; init; }

    public bool? MotherboardPolling { get; init; }

    public bool AcceptUniqueCandidates { get; init; }

    public double? IntervalSeconds { get; init; }

    public int? Count { get; init; }

    public string? ListenAddress { get; init; }

    public int? Port { get; init; }

    public string? TokenFile { get; init; }

    public bool AllowLan { get; init; }

    public string? ShutdownConfigPath { get; init; }
}

public static class CommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "help",
        "list-sensors",
        "snapshot",
        "watch",
        "check-backend",
        "serve",
        "service"
    };

    public static ParsedArgs Parse(string[] args)
    {
        string? command = null;
        string? config = null;
        string? output = null;
        string? log = null;
        var synthetic = false;
        var skipWarmup = false;
        bool? motherboard = null;
        var acceptUnique = false;
        double? interval = null;
        int? count = null;
        string? listen = null;
        int? port = null;
        string? tokenFile = null;
        var allowLan = false;
        string? shutdownConfig = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (Commands.Contains(arg))
            {
                if (command is not null)
                {
                    throw new ConfigException($"Only one command is accepted. Got {command} and {arg}.");
                }

                command = arg;
                continue;
            }

            switch (arg)
            {
                case "--config":
                    config = TakeValue(args, ref i, arg);
                    break;
                case "--output":
                    output = TakeValue(args, ref i, arg);
                    break;
                case "--log":
                    log = TakeValue(args, ref i, arg);
                    break;
                case "--interval":
                    interval = ParseDouble(TakeValue(args, ref i, arg), arg);
                    break;
                case "--count":
                    count = ParseCount(TakeValue(args, ref i, arg));
                    break;
                case "--synthetic":
                    synthetic = true;
                    break;
                case "--skip-warmup":
                    skipWarmup = true;
                    break;
                case "--motherboard-polling":
                    motherboard = true;
                    break;
                case "--no-motherboard-polling":
                    motherboard = false;
                    break;
                case "--accept-unique-candidates":
                    acceptUnique = true;
                    break;
                case "--listen":
                    listen = TakeValue(args, ref i, arg);
                    break;
                case "--port":
                    port = ParseCount(TakeValue(args, ref i, arg));
                    break;
                case "--token-file":
                    tokenFile = TakeValue(args, ref i, arg);
                    break;
                case "--allow-lan":
                    allowLan = true;
                    break;
                case "--shutdown-config":
                    shutdownConfig = TakeValue(args, ref i, arg);
                    break;
                case "--help":
                case "-h":
                    if (command is not null)
                    {
                        throw new ConfigException("Pass help on its own.");
                    }

                    command = "help";
                    break;
                default:
                    throw new ConfigException($"Unknown argument: {arg}");
            }
        }

        return new ParsedArgs
        {
            Command = command,
            ConfigPath = config,
            OutputPath = output,
            LogPath = log,
            Synthetic = synthetic,
            SkipWarmup = skipWarmup,
            MotherboardPolling = motherboard,
            AcceptUniqueCandidates = acceptUnique,
            IntervalSeconds = interval,
            Count = count,
            ListenAddress = listen,
            Port = port,
            TokenFile = tokenFile,
            AllowLan = allowLan,
            ShutdownConfigPath = shutdownConfig
        };
    }

    public static string Help()
    {
        return """
            diego-telemetry-host — read-only Windows sensor diagnostic

            Commands
              list-sensors    Inventory, candidates, and role readings as one JSON document
              snapshot        One JSON sample. sampledOnce is true
              watch           JSON lines at the poll interval. sampledOnce is false
              check-backend   Report whether this build can see PawnIO. Does not open sensors
              serve           Optional token-checked GET /v1/telemetry. Off unless you run it
              service         Windows SCM entry. Preshutdown can ask the Pi to power off
              help            Show this text

            Options
              --config PATH
              --output PATH
              --log PATH                 watch only; JSON lines, appended
              --interval SECONDS         overrides config; minimum 0.5
              --count N                  watch only; stop after N samples
              --synthetic                fixture, never live hardware
              --skip-warmup              do not wait one interval before the first sample
              --motherboard-polling
              --no-motherboard-polling
              --accept-unique-candidates fill a scalar role when exactly one candidate exists
                                         verified stays false
              --listen ADDRESS           serve or service. No default public bind
              --port N                   default 8765 when --listen is set
              --token-file PATH          bearer token file. The token is not a command argument
              --allow-lan                required to bind 0.0.0.0. Does not change the firewall
              --shutdown-config PATH     service only. See host/config/example.shutdown.json

            serve is a foreground listener. It does not register with the SCM, so stopping
            it is not a PC shutdown and it does not ask the Pi to power off.
            service on Windows accepts SERVICE_CONTROL_PRESHUTDOWN. SERVICE_CONTROL_STOP
            only stops this process. HTTP on the LAN is cleartext; use a private network.
            Real sensors require the win-x64 publish on Windows, with PawnIO already installed.
            This program does not install PawnIO, write fan duty, or change Fan Control.
            Exit codes: 0 success or cancelled, 2 usage or config, 3 real hardware unavailable, 4 hardware open failed.
            """;
    }

    private static string TakeValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            throw new ConfigException($"{name} needs a value.");
        }

        index++;
        return args[index];
    }

    private static double ParseDouble(string text, string name)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ConfigException($"{name} must be a number. Got {text}.");
        }

        return value;
    }

    private static int ParseCount(string text)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new ConfigException($"--count must be an integer of 1 or more. Got {text}.");
        }

        return value;
    }
}
