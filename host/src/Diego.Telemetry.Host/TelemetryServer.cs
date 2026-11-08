using System.Net;
using System.Text;

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
    private readonly IHardwareSource _source;
    private readonly HostConfig _config;
    private readonly IClock _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _sequence;
    private string _latest = "";

    private TelemetryServer(string prefix, string token, IHardwareSource source, HostConfig config, IClock clock)
    {
        _token = token;
        _source = source;
        _config = config;
        _clock = clock;
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public string Prefix { get; private set; } = "";

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
        IClock clock)
    {
        ValidateBind(bindAddress, port, allowLan);
        if (string.IsNullOrEmpty(token))
        {
            throw new ConfigException("Refusing to listen without a token.");
        }

        var host = bindAddress is "0.0.0.0" or "*" or "+" ? "+" : bindAddress;
        var prefix = $"http://{host}:{port}/";
        var server = new TelemetryServer(prefix, token, source, config, clock)
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
                return;
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

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            Write(context, 405, ErrorJson("method_not_allowed", "Only GET /v1/telemetry is served."));
            return;
        }

        if (!string.Equals(path, "/v1/telemetry", StringComparison.Ordinal))
        {
            Write(context, 404, ErrorJson("not_found", "Only /v1/telemetry is served."));
            return;
        }

        var header = request.Headers["Authorization"] ?? "";
        const string prefix = "Bearer ";
        var provided = header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..] : "";
        if (!TokenFile.FixedTimeEquals(provided, _token))
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
