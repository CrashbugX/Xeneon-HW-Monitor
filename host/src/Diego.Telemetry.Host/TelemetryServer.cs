using System.Net;
using System.Text;
using System.Text.Json;

namespace Diego.Telemetry.Host;

/// <summary>
/// Optional cleartext HTTP feed of the telemetry document. It is off unless
/// <c>serve</c> or the Windows service is started with a bind address and a token file.
/// The Pi bridge is the only intended client. Browsers should not call this port,
/// so the token never has to live in a page.
/// </summary>
public sealed class TelemetryServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _token;
    private readonly PairingBroker? _pairing;
    private readonly IHardwareSource _source;
    private readonly HostConfig _config;
    private readonly IClock _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _sequence;
    private int _port;
    private string _latest = "";

    private TelemetryServer(string prefix, int port, string token, PairingBroker? pairing, IHardwareSource source, HostConfig config, IClock clock)
    {
        _token = token;
        _pairing = pairing;
        _port = port;
        _source = source;
        _config = config;
        _clock = clock;
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public string Prefix { get; private set; } = "";

    /// <summary>Called after a device record changes so the window can save devices.json.</summary>
    public Action? DevicesChanged { get; set; }

    public int Port => _port;

    /// <summary>
    /// Binds the listener. Refuses every-interface addresses unless <paramref name="allowLan"/> is true.
    /// </summary>
    /// <param name="bindAddress">IP such as 127.0.0.1. 0.0.0.0 requires allowLan.</param>
    /// <param name="port">TCP port, 1 through 65535. Use a high port so admin rights are not required.</param>
    /// <param name="token">Shared secret already read from a file.</param>
    /// <param name="allowLan">Must be true before binding all interfaces. Still requires the bearer token.</param>
    public static TelemetryServer Start(
        string bindAddress,
        int port,
        string token,
        bool allowLan,
        IHardwareSource source,
        HostConfig config,
        IClock clock,
        PairingBroker? pairing = null)
    {
        ValidateBind(bindAddress, port, allowLan);
        if (string.IsNullOrEmpty(token) && pairing is null)
        {
            throw new ConfigException("Refusing to listen without a token.");
        }

        var host = bindAddress is "0.0.0.0" or "*" or "+" ? "+" : bindAddress;
        var prefix = $"http://{host}:{port}/";
        var server = new TelemetryServer(prefix, port, token, pairing, source, config, clock)
        {
            Prefix = prefix
        };
        return server;
    }

    /// <summary>Stops sampling and the listener.</summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _listener.Close();
        await _source.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>Shared bind rule for telemetry and for the shutdown service docs.</summary>
    public static void ValidateBind(string bindAddress, int port, bool allowLan)
    {
        if (port is < 1 or > 65535)
        {
            throw new ConfigException("Port must be from 1 to 65535.");
        }

        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            throw new ConfigException("A bind address is required. There is no default public listener.");
        }

        var any = bindAddress is "0.0.0.0" or "::" or "*" or "+";
        if (any && !allowLan)
        {
            throw new ConfigException(
                "Refusing to listen on all interfaces without --allow-lan. " +
                "That flag still requires the bearer token. It does not open the Windows firewall.");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var accept = AcceptLoop(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var observation = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
                var document = SnapshotBuilder.Build(observation, _config, _clock.UtcNow, sampledOnce: false, _sequence);
                _sequence++;
                _latest = JsonFormat.Serialize(document, indented: false);
                await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                await accept.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested || ex is ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            try
            {
                Handle(context);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? "/";
        if (request.Url?.Query?.Contains("token=", StringComparison.OrdinalIgnoreCase) == true)
        {
            Write(context, 400, ErrorJson("token_in_query", "Send the token in the Authorization header, not the query string."));
            return;
        }

        if (string.Equals(path, "/v1/discover", StringComparison.Ordinal) && string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            Write(context, 200, JsonFormat.Serialize(PublicAnnouncement(), indented: false));
            return;
        }

        if (_pairing is not null && string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            if (HandlePairPost(context, path))
            {
                return;
            }
        }

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            Write(context, 405, ErrorJson("method_not_allowed", "That method is not served on this path."));
            return;
        }

        if (path is "/v1/pair/pending")
        {
            if (!IsLoopback(request))
            {
                Write(context, 403, ErrorJson("local_only", "Pending pairing codes are shown only on the host itself."));
                return;
            }

            Write(context, 200, JsonFormat.Serialize(new { ok = true, pending = _pairing?.Pending ?? [] }, indented: false));
            return;
        }

        if (path is not ("/v1/telemetry" or "/v1/inventory"))
        {
            Write(context, 404, ErrorJson("not_found", "That path is not served."));
            return;
        }

        if (!Authorized(Bearer(request)))
        {
            Write(context, 401, ErrorJson("unauthorized", "Bearer token refused."));
            return;
        }

        if (string.IsNullOrEmpty(_latest))
        {
            Write(context, 503, ErrorJson("waiting", "No sample yet."));
            return;
        }

        Write(context, 200, _latest);
    }

    private bool HandlePairPost(HttpListenerContext context, string path)
    {
        var pairing = _pairing;
        if (pairing is null)
        {
            return false;
        }

        var remote = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
        if (path == "/v1/pair/begin")
        {
            var name = ReadJson(context).TryGetProperty("displayName", out var display) ? display.GetString() ?? "" : "";
            var begun = pairing.Begin(remote, name);
            if (!begun.Ok)
            {
                Write(context, 429, ErrorJson(begun.ErrorCode ?? "rate_limited", begun.ErrorMessage ?? "Pairing is rate limited."));
                return true;
            }

            Write(context, 200, JsonFormat.Serialize(new { ok = true, pairingId = begun.PairingId, expiresAtUtc = begun.ExpiresAtUtc }, indented: false));
            return true;
        }

        if (path == "/v1/pair/approve")
        {
            if (!IsLoopback(context.Request))
            {
                Write(context, 403, ErrorJson("local_only", "Approve pairing on the host window. A remote request cannot approve itself."));
                return true;
            }

            var id = ReadJson(context).TryGetProperty("pairingId", out var pairingId) ? pairingId.GetString() ?? "" : "";
            var error = pairing.Approve(id);
            if (error is not null)
            {
                Write(context, 404, ErrorJson(error.Code, error.Message));
                return true;
            }

            Write(context, 200, JsonFormat.Serialize(new { ok = true }, indented: false));
            return true;
        }

        if (path == "/v1/pair/complete")
        {
            var body = ReadJson(context);
            var id = body.TryGetProperty("pairingId", out var pairingId) ? pairingId.GetString() ?? "" : "";
            var code = body.TryGetProperty("code", out var codeValue) ? codeValue.GetString() ?? "" : "";
            var done = pairing.Complete(id, code, remote);
            if (!done.Ok)
            {
                var status = done.ErrorCode is "bad_code" or "locked" ? 401 : 409;
                Write(context, status, ErrorJson(done.ErrorCode ?? "rejected", done.ErrorMessage ?? "Pairing was not completed."));
                return true;
            }

            Write(context, 200, JsonFormat.Serialize(new { ok = true, deviceId = done.DeviceId, deviceToken = done.Token, name = done.Name }, indented: false));
            DevicesChanged?.Invoke();
            return true;
        }

        if (path == "/v1/control/register")
        {
            // The Pi tells us its poweroff token. Its address is the TCP peer, never a field in the body.
            var bearer = Bearer(context.Request);
            if (!Authorized(bearer))
            {
                Write(context, 401, ErrorJson("unauthorized", "Bearer token refused."));
                return true;
            }

            var body = ReadJson(context);
            var controlPort = body.TryGetProperty("controlPort", out var portValue) && portValue.TryGetInt32(out var parsed) ? parsed : 0;
            var shutdownToken = body.TryGetProperty("shutdownToken", out var tokenValue) ? tokenValue.GetString() ?? "" : "";
            var error = pairing.RememberControl(bearer, remote, controlPort, shutdownToken);
            if (error is not null)
            {
                Write(context, 400, ErrorJson("control", error));
                return true;
            }

            DevicesChanged?.Invoke();
            Write(context, 200, JsonFormat.Serialize(new { ok = true }, indented: false));
            return true;
        }

        if (path == "/v1/pair/revoke")
        {
            if (!IsLoopback(context.Request))
            {
                Write(context, 403, ErrorJson("local_only", "Revoke a device from the host window."));
                return true;
            }

            var id = ReadJson(context).TryGetProperty("deviceId", out var deviceId) ? deviceId.GetString() ?? "" : "";
            var removed = pairing.Revoke(id);
            if (removed)
            {
                DevicesChanged?.Invoke();
            }

            Write(context, removed ? 200 : 404, JsonFormat.Serialize(new { ok = removed }, indented: false));
            return true;
        }

        return false;
    }

    private bool Authorized(string provided)
    {
        if (!string.IsNullOrEmpty(_token) && TokenFile.FixedTimeEquals(provided, _token))
        {
            return true;
        }

        return _pairing?.Authorize(provided) == true;
    }

    private DiscoveryAnnouncement PublicAnnouncement()
    {
        return new DiscoveryAnnouncement
        {
            Name = Environment.MachineName,
            TelemetryPort = _port,
            Product = ProductInfo.Name,
            Version = ProductInfo.Version
        };
    }

    private static string Bearer(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"] ?? "";
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..] : "";
    }

    private static bool IsLoopback(HttpListenerRequest request)
    {
        var address = request.RemoteEndPoint?.Address;
        return address is not null && IPAddress.IsLoopback(address);
    }

    private static JsonElement ReadJson(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        if (text.Length > 4096)
        {
            text = "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }

    private static string ErrorJson(string code, string message)
    {
        return JsonFormat.Serialize(new
        {
            schemaVersion = TelemetrySchema.Version,
            ok = false,
            error = new { code, message }
        }, indented: false);
    }

    private static void Write(HttpListenerContext context, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }
}
