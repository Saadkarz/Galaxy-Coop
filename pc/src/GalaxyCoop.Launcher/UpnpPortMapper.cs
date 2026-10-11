using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GalaxyCoop.Launcher;

/// <summary>
/// Opens the game port on the home router with UPnP IGD, so friends can reach a
/// server behind NAT without manual port forwarding. Best effort: many routers
/// have UPnP turned off, in which case hosting still works on the LAN and for
/// anyone you forward the port for by hand.
/// </summary>
public sealed class UpnpPortMapper : IAsyncDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    readonly Uri _controlUrl;
    readonly string _serviceType;
    readonly int _port;

    public IPAddress? ExternalAddress { get; private set; }
    public IPAddress LocalAddress { get; }

    UpnpPortMapper(Uri controlUrl, string serviceType, int port, IPAddress local)
    {
        _controlUrl = controlUrl;
        _serviceType = serviceType;
        _port = port;
        LocalAddress = local;
    }

    /// <summary>Maps UDP <paramref name="port"/> to this PC; returns null (with a reason) if no router agreed.</summary>
    public static async Task<(UpnpPortMapper? mapper, string message)> TryMapAsync(int port, string description, CancellationToken ct)
    {
        try
        {
            foreach (var location in await DiscoverAsync(ct))
            {
                var service = await FindWanServiceAsync(location, ct);
                if (service is null) continue;
                var local = LocalAddressTowards(location.Host);
                var mapper = new UpnpPortMapper(service.Value.control, service.Value.type, port, local);
                if (!await mapper.AddAsync(description, 0, ct) && !await mapper.AddAsync(description, 7200, ct))
                    return (null, "the router refused the UPnP port mapping");
                mapper.ExternalAddress = await mapper.GetExternalAddressAsync(ct);
                return (mapper, $"router opened UDP {port} (UPnP){(mapper.ExternalAddress is { } ip ? $", public address {ip}" : "")}");
            }
            return (null, "no UPnP router answered; forward the port by hand for internet play");
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return (null, $"UPnP failed: {ex.Message}");
        }
    }

    static async Task<List<Uri>> DiscoverAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var st in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:service:WANIPConnection:1" })
        {
            var request = Encoding.ASCII.GetBytes(
                $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n");
            await udp.SendAsync(request, target, ct);
        }

        var found = new List<Uri>();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(2.5));
        try
        {
            while (true)
            {
                var reply = await udp.ReceiveAsync(window.Token);
                var m = Regex.Match(Encoding.ASCII.GetString(reply.Buffer), @"^LOCATION:\s*(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (m.Success && Uri.TryCreate(m.Groups[1].Value, UriKind.Absolute, out var uri) && !found.Contains(uri)) found.Add(uri);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return found;
    }

    static async Task<(Uri control, string type)?> FindWanServiceAsync(Uri location, CancellationToken ct)
    {
        var doc = XDocument.Parse(await Http.GetStringAsync(location, ct));
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var baseUri = doc.Root?.Element(ns + "URLBase")?.Value is { Length: > 0 } b ? new Uri(b) : location;
        foreach (var service in doc.Descendants(ns + "service"))
        {
            var type = service.Element(ns + "serviceType")?.Value ?? "";
            if (!type.Contains("WANIPConnection") && !type.Contains("WANPPPConnection")) continue;
            if (service.Element(ns + "controlURL")?.Value is { Length: > 0 } control)
                return (new Uri(baseUri, control), type);
        }
        return null;
    }

    static IPAddress LocalAddressTowards(string host)
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Connect(host, 1900);
        return ((IPEndPoint)probe.LocalEndPoint!).Address;
    }

    async Task<string?> SoapAsync(string action, string arguments, CancellationToken ct)
    {
        var body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                   "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                   $"<u:{action} xmlns:u=\"{_serviceType}\">{arguments}</u:{action}></s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, _controlUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{_serviceType}#{action}\"");
        using var response = await Http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    async Task<bool> AddAsync(string description, int lease, CancellationToken ct) => await SoapAsync("AddPortMapping",
        $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{_port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
        $"<NewInternalPort>{_port}</NewInternalPort><NewInternalClient>{LocalAddress}</NewInternalClient><NewEnabled>1</NewEnabled>" +
        $"<NewPortMappingDescription>{System.Security.SecurityElement.Escape(description)}</NewPortMappingDescription>" +
        $"<NewLeaseDuration>{lease}</NewLeaseDuration>", ct) is not null;

    async Task<IPAddress?> GetExternalAddressAsync(CancellationToken ct)
    {
        var xml = await SoapAsync("GetExternalIPAddress", "", ct);
        var m = xml is null ? null : Regex.Match(xml, @"<NewExternalIPAddress>([^<]+)</NewExternalIPAddress>");
        return m is { Success: true } && IPAddress.TryParse(m.Groups[1].Value, out var ip) ? ip : null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SoapAsync("DeletePortMapping",
                $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{_port}</NewExternalPort><NewProtocol>UDP</NewProtocol>", cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
    }
}
