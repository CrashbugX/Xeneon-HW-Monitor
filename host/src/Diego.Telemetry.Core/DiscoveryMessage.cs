// LAN discovery payload. It names the host and the port. It does not carry a token.
using System.Text.Json;

namespace Diego.Telemetry;

/// <summary>
/// Small UDP discovery document. mDNS on port 5353 collides with Avahi and with
/// Windows DNS-SD, so both sides speak this JSON on UDP port 8764 instead.
/// A network that blocks multicast still works: the Pi can type the host address,
/// and the host answers <c>GET /v1/discover</c> with the same document.
/// </summary>
public static class DiscoveryMessage
{
    public const string Magic = "xeneon-discover-1";

    public const int DefaultPort = 8764;

    public static byte[] Query()
    {
        return Encode(new Dictionary<string, object?>
        {
            ["magic"] = Magic,
            ["op"] = "query"
        });
    }

    public static byte[] Announce(DiscoveryAnnouncement announcement)
    {
        return Encode(new Dictionary<string, object?>
        {
            ["magic"] = Magic,
            ["op"] = "announce",
            ["name"] = announcement.Name,
            ["port"] = announcement.TelemetryPort,
            ["product"] = announcement.Product,
            ["version"] = announcement.Version,
            ["pairing"] = true
        });
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out DiscoveryPacket packet)
    {
        packet = new DiscoveryPacket(false, "", null);
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("magic", out var magic) || magic.GetString() != Magic)
            {
                return false;
            }

            var op = root.TryGetProperty("op", out var operation) ? operation.GetString() ?? "" : "";
            if (op == "query")
            {
                packet = new DiscoveryPacket(true, "query", null);
                return true;
            }

            if (op != "announce")
            {
                return false;
            }

            var announcement = new DiscoveryAnnouncement
            {
                Name = root.GetProperty("name").GetString() ?? "",
                TelemetryPort = root.GetProperty("port").GetInt32(),
                Product = root.GetProperty("product").GetString() ?? "",
                Version = root.GetProperty("version").GetString() ?? ""
            };
            packet = new DiscoveryPacket(true, "announce", announcement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Encode(Dictionary<string, object?> payload)
    {
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }
}

public sealed class DiscoveryAnnouncement
{
    public required string Name { get; init; }

    public required int TelemetryPort { get; init; }

    public required string Product { get; init; }

    public required string Version { get; init; }
}

public readonly record struct DiscoveryPacket(bool Ok, string Op, DiscoveryAnnouncement? Announcement);
