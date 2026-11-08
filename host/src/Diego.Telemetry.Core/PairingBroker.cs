// One-time pairing. The code stays on the host until a person approves it.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Diego.Telemetry;

/// <summary>
/// Issues a long-lived device token only after a person at the host approves
/// a pending request and the short code matches. Nothing is approved by the
/// arrival of a network request. The raw token is returned once and only its
/// SHA-256 is stored.
/// </summary>
public sealed class PairingBroker
{
    public const int CodeLifetimeSeconds = 120;

    public const int MaxCodeAttempts = 5;

    public const int MaxBeginsPerWindow = 5;

    public static readonly TimeSpan BeginWindow = TimeSpan.FromMinutes(10);

    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly List<PendingRequest> _pending = [];
    private readonly List<DeviceRecord> _devices = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _begins = new(StringComparer.Ordinal);

    public PairingBroker(IClock clock)
    {
        _clock = clock;
    }

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Select(device => new PairedDevice
                {
                    Id = device.Id,
                    Name = device.Name,
                    PairedAtUtc = device.PairedAtUtc,
                    LastSeenUtc = device.LastSeenUtc
                }).ToArray();
            }
        }
    }

    public IReadOnlyList<PendingPairing> Pending
    {
        get
        {
            lock (_gate)
            {
                Expire();
                return _pending.Select(ToPublic).ToArray();
            }
        }
    }

    /// <summary>Starts a request. The code is not in the result. Call <see cref="Pending"/> on the host.</summary>
    public PairBegin Begin(string remoteKey, string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Pi" : displayName.Trim();
        if (name.Length > 64)
        {
            name = name[..64];
        }

        lock (_gate)
        {
            Expire();
            var key = string.IsNullOrWhiteSpace(remoteKey) ? "unknown" : remoteKey;
            if (!_begins.TryGetValue(key, out var stamps))
            {
                stamps = [];
                _begins[key] = stamps;
            }

            stamps.RemoveAll(stamp => _clock.UtcNow - stamp > BeginWindow);
            if (stamps.Count >= MaxBeginsPerWindow)
            {
                return new PairBegin(false, null, null, "rate_limited", "Too many pairing requests from this address. Wait and try again.");
            }

            stamps.Add(_clock.UtcNow);
            var pending = new PendingRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
                Name = name,
                RemoteKey = key,
                ExpiresAtUtc = _clock.UtcNow.AddSeconds(CodeLifetimeSeconds)
            };
            _pending.Add(pending);
            return new PairBegin(true, pending.Id, pending.ExpiresAtUtc, null, null);
        }
    }

    /// <summary>Host-local approval. A remote caller must not be allowed to invoke this.</summary>
    public PairingError? Approve(string pairingId)
    {
        lock (_gate)
        {
            Expire();
            var pending = _pending.FirstOrDefault(item => item.Id == pairingId);
            if (pending is null)
            {
                return new PairingError("not_found", "That pairing request has expired or does not exist.");
            }

            pending.Approved = true;
            return null;
        }
    }

    public PairComplete Complete(string pairingId, string code, string remoteKey)
    {
        lock (_gate)
        {
            Expire();
            var pending = _pending.FirstOrDefault(item => item.Id == pairingId);
            if (pending is null)
            {
                return PairComplete.Fail("not_found", "That pairing request has expired or does not exist.");
            }

            if (!string.Equals(pending.RemoteKey, remoteKey, StringComparison.Ordinal))
            {
                return PairComplete.Fail("wrong_client", "Finish pairing from the same machine that started it.");
            }

            if (pending.Attempts >= MaxCodeAttempts)
            {
                _pending.Remove(pending);
                return PairComplete.Fail("locked", "Too many wrong codes. Start pairing again.");
            }

            if (!FixedEquals(code.Trim(), pending.Code))
            {
                pending.Attempts++;
                if (pending.Attempts >= MaxCodeAttempts)
                {
                    _pending.Remove(pending);
                    return PairComplete.Fail("locked", "Too many wrong codes. Start pairing again.");
                }

                return PairComplete.Fail("bad_code", "That code does not match. The host did not approve a guess.");
            }

            if (!pending.Approved)
            {
                return PairComplete.Fail("awaiting_approval", "Approve the request on the Windows host, then enter the code again.");
            }

            var token = NewToken();
            var record = new DeviceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = pending.Name,
                TokenHash = Hash(token),
                PairedAtUtc = _clock.UtcNow,
                LastSeenUtc = _clock.UtcNow
            };
            _devices.Add(record);
            _pending.Remove(pending);
            return new PairComplete(true, record.Id, token, record.Name, null, null);
        }
    }

    public bool Authorize(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var hash = Hash(token);
        lock (_gate)
        {
            var device = _devices.FirstOrDefault(item => FixedEquals(item.TokenHash, hash));
            if (device is null)
            {
                return false;
            }

            device.LastSeenUtc = _clock.UtcNow;
            return true;
        }
    }

    public bool Revoke(string deviceId)
    {
        lock (_gate)
        {
            var device = _devices.FirstOrDefault(item => item.Id == deviceId);
            if (device is null)
            {
                return false;
            }

            _devices.Remove(device);
            return true;
        }
    }

    public void Save(string path)
    {
        DeviceRecord[] copy;
        lock (_gate)
        {
            copy = _devices.Select(device => new DeviceRecord
            {
                Id = device.Id,
                Name = device.Name,
                TokenHash = device.TokenHash,
                PairedAtUtc = device.PairedAtUtc,
                LastSeenUtc = device.LastSeenUtc
            }).ToArray();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new DeviceFile { Devices = copy.ToList() }, JsonFormat.Pretty);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var file = JsonSerializer.Deserialize<DeviceFile>(File.ReadAllText(path), JsonFormat.Config);
        lock (_gate)
        {
            _devices.Clear();
            if (file?.Devices is null)
            {
                return;
            }

            foreach (var device in file.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.Id) || string.IsNullOrWhiteSpace(device.TokenHash))
                {
                    continue;
                }

                if (device.TokenHash.Length < 32)
                {
                    continue;
                }

                _devices.Add(device);
            }
        }
    }

    private void Expire()
    {
        _pending.RemoveAll(item => item.ExpiresAtUtc <= _clock.UtcNow);
    }

    private static PendingPairing ToPublic(PendingRequest pending)
    {
        return new PendingPairing
        {
            Id = pending.Id,
            Name = pending.Name,
            Code = pending.Code,
            Approved = pending.Approved,
            ExpiresAtUtc = pending.ExpiresAtUtc,
            Attempts = pending.Attempts
        };
    }

    private static string NewToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    public static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed class PendingRequest
    {
        public required string Id { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }

        public required string RemoteKey { get; init; }

        public required DateTimeOffset ExpiresAtUtc { get; init; }

        public bool Approved { get; set; }

        public int Attempts { get; set; }
    }

    private sealed class DeviceFile
    {
        public List<DeviceRecord> Devices { get; set; } = [];
    }
}

public sealed class DeviceRecord
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string TokenHash { get; set; } = "";

    public DateTimeOffset PairedAtUtc { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }
}

public sealed class PairedDevice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset PairedAtUtc { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }
}

public sealed class PendingPairing
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Code { get; init; }

    public required bool Approved { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required int Attempts { get; init; }
}

public sealed record PairBegin(bool Ok, string? PairingId, DateTimeOffset? ExpiresAtUtc, string? ErrorCode, string? ErrorMessage);

public sealed record PairComplete(bool Ok, string? DeviceId, string? Token, string? Name, string? ErrorCode, string? ErrorMessage)
{
    public static PairComplete Fail(string code, string message)
    {
        return new PairComplete(false, null, null, null, code, message);
    }
}

public sealed record PairingError(string Code, string Message);
