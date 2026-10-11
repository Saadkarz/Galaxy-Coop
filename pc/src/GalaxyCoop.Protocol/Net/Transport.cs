using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace GalaxyCoop.Protocol.Net;

public interface ITransport
{
    void Send(IPEndPoint to, byte[] data);
}

public sealed class UdpTransport : ITransport, IDisposable
{
    readonly UdpClient _udp;

    public UdpTransport(int port = 0)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        if (OperatingSystem.IsWindows())
        {
            // Without this, an ICMP "port unreachable" from a peer that closed makes the next receive throw.
            const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
            _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[4], null);
        }
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public void Send(IPEndPoint to, byte[] data)
    {
        try { _udp.Send(data, data.Length, to); }
        catch (SocketException) { }
    }

    public async Task<UdpReceiveResult?> ReceiveAsync(CancellationToken ct)
    {
        try { return await _udp.ReceiveAsync(ct); }
        catch (SocketException) { return null; }
    }

    public void Dispose() => _udp.Dispose();

    public static IPEndPoint ParseEndpoint(string text, int defaultPort)
    {
        string host = text;
        int port = defaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && !text.EndsWith(']') && text.IndexOf(':') == colon)
        {
            host = text[..colon];
            port = int.Parse(text[(colon + 1)..]);
        }
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return new IPEndPoint(ip, port);
        var addrs = Dns.GetHostAddresses(host);
        var pick = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.First();
        return new IPEndPoint(pick, port);
    }
}
