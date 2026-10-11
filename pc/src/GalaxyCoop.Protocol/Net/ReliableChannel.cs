namespace GalaxyCoop.Protocol.Net;

/// <summary>
/// One direction-pair of ordered, exactly-once delivery over UDP. The sender
/// keeps every packet until it is acked and resends on a fixed interval; the
/// receiver acks everything it sees (duplicates included, in case the first
/// ack was lost) and releases payloads strictly in sequence order.
/// </summary>
public sealed class ReliableChannel
{
    const uint ReceiveWindow = 4096;

    sealed class Pending(byte[] packet, long sentAt)
    {
        public readonly byte[] Packet = packet;
        public long LastSent = sentAt;
        public int Attempts = 1;
    }

    readonly SortedDictionary<uint, Pending> _pending = new();
    readonly Dictionary<uint, byte[]> _early = new();
    uint _nextSend = 1;
    uint _nextExpected = 1;

    public int ResendIntervalMs { get; init; } = 150;
    public int MaxAttempts { get; init; } = 60;

    /// <summary>Set once a packet went unacked for MaxAttempts resends; the peer should be considered gone.</summary>
    public bool Failed { get; private set; }
    public int PendingCount => _pending.Count;

    public byte[] Send(ReliableMessage message, long nowMs) => Send(Codec.EncodeReliable(message), nowMs);

    public byte[] Send(byte[] payload, long nowMs)
    {
        uint seq = _nextSend++;
        var packet = Codec.Encode(new ReliablePacket(seq, payload));
        _pending[seq] = new Pending(packet, nowMs);
        return packet;
    }

    public List<byte[]> CollectResends(long nowMs)
    {
        var due = new List<byte[]>();
        foreach (var p in _pending.Values)
        {
            if (nowMs - p.LastSent < ResendIntervalMs) continue;
            if (++p.Attempts > MaxAttempts) Failed = true;
            p.LastSent = nowMs;
            due.Add(p.Packet);
        }
        return due;
    }

    public void OnAck(Ack ack)
    {
        foreach (var s in ack.Sequences) _pending.Remove(s);
    }

    /// <summary>Returns the messages that became deliverable, in order. Always ack the packet's sequence afterwards.</summary>
    public List<ReliableMessage> Receive(ReliablePacket packet)
    {
        var delivered = new List<ReliableMessage>();
        uint seq = packet.Sequence;
        if (seq < _nextExpected || seq - _nextExpected > ReceiveWindow) return delivered;
        _early.TryAdd(seq, packet.Payload);
        while (_early.Remove(_nextExpected, out var payload))
        {
            _nextExpected++;
            if (Codec.DecodeReliable(payload) is ReliableMessage m) delivered.Add(m);
        }
        return delivered;
    }
}
