// Answers discovery queries on UDP. The payload is DiscoveryMessage, which has no token.
using System.Net;
using System.Net.Sockets;

namespace Diego.Telemetry.Host;

/// <summary>
/// Listens for a discovery query and replies to the sender. Broadcast and
/// unicast both work. The reply is the same document as <c>GET /v1/discover</c>.
/// </summary>
public sealed class DiscoveryBeacon : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Func<DiscoveryAnnouncement> _current;
    private readonly CancellationTokenSource _cts = new();

    public DiscoveryBeacon(int port, Func<DiscoveryAnnouncement> current)
    {
        _current = current;
        Port = port;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.EnableBroadcast = true;
        _ = Task.Run(Loop);
    }

    public int Port { get; }

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            if (!DiscoveryMessage.TryParse(received.Buffer, out var packet) || packet.Op != "query")
            {
                continue;
            }

            var bytes = DiscoveryMessage.Announce(_current());
            try
            {
                await _udp.SendAsync(bytes, received.RemoteEndPoint, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}
