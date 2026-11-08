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
}

public static class CommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "help",
        "list-sensors",
        "snapshot",
        "watch",
        "check-backend"
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
            Count = count
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
