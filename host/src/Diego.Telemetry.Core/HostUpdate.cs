// Finds a newer host installer on the public GitHub release list.
// Nothing here downloads a file or starts setup. The window does that only
// after Update now is clicked.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Diego.Telemetry;

/// <summary>
/// Release shape for the Windows host. A client release in the same repository
/// is ignored unless it carries the host installer asset.
/// </summary>
/// <remarks>
/// Publish a release tagged <c>host-vX.Y.Z</c>, for example <c>host-v0.3.0</c>.
/// Attach <c>XeneonHwMonitorHost-X.Y.Z-win-x64.exe</c>.
/// Publish the SHA-256 as a line in the release body, <c>SHA-256: </c> plus 64 hex
/// digits, or as a second asset named <c>XeneonHwMonitorHost-X.Y.Z-win-x64.exe.sha256</c>.
/// A historical tag <c>vX.Y.Z</c> is accepted only when that same installer asset is attached,
/// which is how 0.2.0 was published. Prereleases are skipped. The check never installs.
/// </remarks>
public static class HostUpdate
{
    public const string Repository = "CrashbugX/Xeneon-HW-Monitor";

    public const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30";

    public const string TagPrefix = "host-v";

    public const string OfflineMessage = "No update check. The PC is offline or GitHub did not answer.";

    public const string NoReleaseMessage = "No host release was published.";

    public static string CurrentMessage(string version)
    {
        return "Xeneon HW Monitor " + version + " is the newest host release found.";
    }

    /// <summary>True for the GitHub URLs this program is willing to request. Redirects stay on GitHub's hosts.</summary>
    public static bool IsAllowedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != "https")
        {
            return false;
        }

        var host = parsed.Host;
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return parsed.AbsolutePath.StartsWith("/" + Repository + "/", StringComparison.Ordinal);
        }

        if (host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return parsed.AbsolutePath.StartsWith("/repos/" + Repository + "/", StringComparison.Ordinal);
        }

        return host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks the newest host installer that is newer than <paramref name="currentVersion"/>.
    /// Returns null when the list has no newer host release. Throws when the JSON is not a release array.
    /// </summary>
    public static UpdateOffer? FindNewer(string releasesJson, string currentVersion)
    {
        using var document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("GitHub releases were not a list.");
        }

        UpdateOffer? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
            var asset = FindInstaller(release);
            if (asset is null || !TryVersionFromTag(tag, asset.Value.Name, out var version))
            {
                continue;
            }

            if (CompareVersions(version, currentVersion) <= 0)
            {
                continue;
            }

            if (best is not null && CompareVersions(version, best.Version) <= 0)
            {
                continue;
            }

            var body = release.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? "" : "";
            var sha = TryReadSha256(body, asset.Value.Name);
            best = new UpdateOffer
            {
                Tag = tag,
                Version = version,
                Notes = NotesWithoutChecksum(body),
                AssetName = asset.Value.Name,
                DownloadUrl = asset.Value.Url,
                Size = asset.Value.Size,
                Sha256 = sha,
                Sha256Url = FindShaAsset(release, asset.Value.Name + ".sha256")
            };
        }

        return best;
    }

    public static int CompareVersions(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        var count = Math.Max(a.Length, b.Length);
        for (var i = 0; i < count; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }

        return 0;
    }

    /// <summary>Hex from a release body line, or empty when the body does not publish one.</summary>
    public static string TryReadSha256(string releaseBody, string assetName)
    {
        foreach (var raw in releaseBody.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = line["SHA-256:".Length..].Trim();
            var pieces = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0 || !IsHex64(pieces[0]))
            {
                continue;
            }

            if (pieces.Length == 1 || pieces[1].Equals(assetName, StringComparison.OrdinalIgnoreCase))
            {
                return pieces[0].ToLowerInvariant();
            }
        }

        return "";
    }

    /// <summary>Hex from a .sha256 asset. A trailing filename is allowed. Empty when the text is not a digest.</summary>
    public static string TryReadSha256File(string text)
    {
        var piece = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return IsHex64(piece) ? piece.ToLowerInvariant() : "";
    }

    /// <summary>Empty when the file matches. The reason otherwise. The file is not executed.</summary>
    public static string VerifyFile(string path, long expectedSize, string expectedSha256)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return "The installer download is missing.";
        }

        if (expectedSize > 0 && info.Length != expectedSize)
        {
            return "The download is " + info.Length + " bytes. The release says " + expectedSize + ".";
        }

        if (!IsHex64(expectedSha256))
        {
            return "This release did not publish a SHA-256. Update now stays off.";
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "The SHA-256 does not match the release. The installer was not started.";
        }

        return "";
    }

    private static bool TryVersionFromTag(string tag, string assetName, out string version)
    {
        version = "";
        var fromName = VersionInAsset(assetName);
        if (fromName.Length == 0)
        {
            return false;
        }

        if (tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            version = tag[TagPrefix.Length..].TrimStart('v', 'V');
            return SameVersion(version, fromName);
        }

        // v0.2.0 was published without the host- prefix. Accept that shape only
        // when the asset is the host installer, so a client tag is not an update.
        if (tag.StartsWith('v') || tag.StartsWith('V'))
        {
            version = tag[1..];
            return SameVersion(version, fromName);
        }

        return false;
    }

    private static bool SameVersion(string tagVersion, string assetVersion)
    {
        return CompareVersions(tagVersion, assetVersion) == 0;
    }

    private static string VersionInAsset(string name)
    {
        var match = Regex.Match(name, @"^XeneonHwMonitorHost-(\d+\.\d+\.\d+)-win-x64\.exe$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static (string Name, string Url, long Size)? FindInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            if (VersionInAsset(name).Length == 0)
            {
                continue;
            }

            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
            if (!IsAllowedDownloadUrl(url) || size <= 0)
            {
                return null;
            }

            return (name, url, size);
        }

        return null;
    }

    private static string FindShaAsset(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out var assets))
        {
            return "";
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            if (!assetName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() ?? "" : "";
            return IsAllowedDownloadUrl(url) ? url : "";
        }

        return "";
    }

    private static string NotesWithoutChecksum(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var kept = lines.Where(line => !line.TrimStart().StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase));
        var text = string.Join("\n", kept).Trim();
        return text.Length <= 1200 ? text : text[..1200];
    }

    private static int[] Parts(string version)
    {
        var cleaned = version.Trim().TrimStart('v', 'V');
        return cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .ToArray();
    }

    private static bool IsHex64(string text)
    {
        return text.Length == 64 && text.All(Uri.IsHexDigit);
    }
}

/// <summary>One newer host release. Sha256 may be empty until the .sha256 asset is read.</summary>
public sealed class UpdateOffer
{
    public required string Tag { get; init; }

    public required string Version { get; init; }

    public required string Notes { get; init; }

    public required string AssetName { get; init; }

    public required string DownloadUrl { get; init; }

    public required long Size { get; init; }

    public string Sha256 { get; set; } = "";

    public string Sha256Url { get; init; } = "";

    public bool HasChecksum => HostUpdate.TryReadSha256File(Sha256).Length == 64 || Sha256.Length == 64 || Sha256Url.Length > 0;
}
