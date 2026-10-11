using System.Net;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Tests;

/// <summary>Delivers datagrams between in-process endpoints, optionally dropping and duplicating some.</summary>
public sealed class FakeNetwork
{
    readonly Random _rng;
    readonly List<(IPEndPoint from, IPEndPoint to, byte[] data)> _inFlight = new();
    readonly Dictionary<IPEndPoint, Action<IPEndPoint, byte[]>> _receivers = new();

    public double DropRate { get; set; }
    public double DuplicateRate { get; set; }
    public bool Reorder { get; set; }
    public int Sent { get; private set; }

    public FakeNetwork(int seed = 1) => _rng = new Random(seed);

    public Endpoint Join(int port, Action<IPEndPoint, byte[]> receive)
    {
        var ep = new IPEndPoint(IPAddress.Loopback, port);
        _receivers[ep] = receive;
        return new Endpoint(this, ep);
    }

    public void Leave(IPEndPoint ep) => _receivers.Remove(ep);

    public void Pump(int rounds = 8)
    {
        for (int i = 0; i < rounds && _inFlight.Count > 0; i++)
        {
            var batch = _inFlight.ToList();
            _inFlight.Clear();
            if (Reorder) batch = batch.OrderBy(_ => _rng.Next()).ToList();
            foreach (var (from, to, data) in batch)
                if (_receivers.TryGetValue(to, out var r)) r(from, data);
        }
    }

    public sealed class Endpoint(FakeNetwork net, IPEndPoint address) : ITransport
    {
        public IPEndPoint Address => address;

        public void Send(IPEndPoint to, byte[] data)
        {
            net.Sent++;
            if (net._rng.NextDouble() < net.DropRate) return;
            net._inFlight.Add((address, to, data));
            if (net._rng.NextDouble() < net.DuplicateRate) net._inFlight.Add((address, to, data));
        }
    }
}
