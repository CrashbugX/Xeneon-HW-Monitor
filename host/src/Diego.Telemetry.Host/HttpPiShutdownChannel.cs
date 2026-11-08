using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Diego.Telemetry.Host;

/// <summary>
/// HTTP client for the Pi poweroff route. Cleartext on a private LAN.
/// The token is sent only in the Authorization header.
/// </summary>
public sealed class HttpPiShutdownChannel : IPiShutdownChannel, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token;

    public HttpPiShutdownChannel(string baseUrl, string token, TimeSpan timeout)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        _http = new HttpClient { Timeout = timeout };
    }

    /// <summary>POST /v1/poweroff. A transport failure is Unreachable, not success.</summary>
    public Task<PiShutdownObservation> PostPoweroffAsync(string nonce, DateTimeOffset issuedAtUtc, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            action = "poweroff",
            nonce,
            issuedAtUtc = issuedAtUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });
        return SendAsync(HttpMethod.Post, "/v1/poweroff", body, cancellationToken);
    }

    /// <summary>GET /v1/poweroff/status. Connection loss after an ACK stays Unreachable.</summary>
    public Task<PiShutdownObservation> GetStatusAsync(CancellationToken cancellationToken)
    {
        return SendAsync(HttpMethod.Get, "/v1/poweroff/status", null, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private async Task<PiShutdownObservation> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Map(response.StatusCode, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PiShutdownObservation(PiShutdownPhase.Unreachable, "no response");
        }
    }

    private static PiShutdownObservation Map(System.Net.HttpStatusCode status, string text)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized)
        {
            return new PiShutdownObservation(PiShutdownPhase.Unauthorized, "unauthorized");
        }

        if ((int)status == 409)
        {
            return new PiShutdownObservation(PiShutdownPhase.Replay, "replay");
        }

        if ((int)status == 403)
        {
            return new PiShutdownObservation(PiShutdownPhase.Rejected, "rejected");
        }

        string? state = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("state", out var property))
            {
                state = property.GetString();
            }
        }
        catch (JsonException)
        {
            return new PiShutdownObservation(PiShutdownPhase.Rejected, "bad json");
        }

        return state switch
        {
            "accepted" => new PiShutdownObservation(PiShutdownPhase.Accepted, "accepted"),
            "syncing" => new PiShutdownObservation(PiShutdownPhase.Syncing, "syncing"),
            "halt-issued" => new PiShutdownObservation(PiShutdownPhase.HaltIssued, "halt-issued"),
            "helper-missing" => new PiShutdownObservation(PiShutdownPhase.HelperMissing, "helper-missing"),
            _ => new PiShutdownObservation(PiShutdownPhase.Rejected, "unknown state")
        };
    }
}
