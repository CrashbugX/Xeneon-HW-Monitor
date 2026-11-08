using System.Security.Cryptography;

namespace Diego.Telemetry.Host;

/// <summary>
/// Reads a bearer token from a file. The token is never placed on the command line.
/// </summary>
public static class TokenFile
{
    /// <summary>
    /// Reads the first line and trims it.
    /// </summary>
    /// <param name="path">File the operator created outside the repository.</param>
    /// <param name="warning">Set when the Unix mode is group- or world-readable. Does not include the token.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ConfigException">The path is missing, unreadable, or empty.</exception>
    public static string Read(string path, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigException("A token file path is required. The token is not accepted as a command argument.");
        }

        if (!File.Exists(path))
        {
            throw new ConfigException($"Token file was not found: {path}");
        }

        WarnIfLoosePermissions(path, ref warning);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Token file could not be read: {ex.Message}");
        }

        var line = text.Split('\n', '\r')[0].Trim();
        if (line.Length == 0)
        {
            throw new ConfigException("Token file is empty.");
        }

        return line;
    }

    /// <summary>Compares two tokens without returning early on the first different byte.</summary>
    public static bool FixedTimeEquals(string provided, string expected)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(provided);
        var right = System.Text.Encoding.UTF8.GetBytes(expected);
        var sameLength = left.Length == right.Length;
        var width = Math.Max(left.Length, right.Length);
        if (width == 0)
        {
            return false;
        }

        var a = new byte[width];
        var b = new byte[width];
        left.AsSpan(0, Math.Min(left.Length, width)).CopyTo(a);
        right.AsSpan(0, Math.Min(right.Length, width)).CopyTo(b);
        return CryptographicOperations.FixedTimeEquals(a, b) && sameLength;
    }

    private static void WarnIfLoosePermissions(string path, ref string? warning)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
            {
                warning = "Token file is readable by the group or by others. Mode 600 is recommended. The token was not printed.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            warning = "Token file permissions could not be checked. The token was not printed.";
        }
    }
}
