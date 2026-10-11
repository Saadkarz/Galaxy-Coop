using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace GalaxyCoop.Protocol.Net;

/// <summary>Little-endian packet builder. Network packets are PC-to-PC, so they use host order, unlike the mailbox.</summary>
public sealed class PacketWriter
{
    byte[] _buf = new byte[256];
    public int Length { get; private set; }

    void Ensure(int n)
    {
        if (Length + n <= _buf.Length) return;
        Array.Resize(ref _buf, Math.Max(_buf.Length * 2, Length + n));
    }

    public PacketWriter U8(byte v) { Ensure(1); _buf[Length++] = v; return this; }
    public PacketWriter Bool(bool v) => U8((byte)(v ? 1 : 0));
    public PacketWriter U16(ushort v) { Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(Length), v); Length += 2; return this; }
    public PacketWriter U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(Length), v); Length += 4; return this; }
    public PacketWriter S32(int v) => U32(unchecked((uint)v));
    public PacketWriter F32(float v) { Ensure(4); BinaryPrimitives.WriteSingleLittleEndian(_buf.AsSpan(Length), v); Length += 4; return this; }
    public PacketWriter Vec(Vector3 v) => F32(v.X).F32(v.Y).F32(v.Z);

    public PacketWriter Str(string? s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        if (bytes.Length > 255) bytes = bytes[..255];
        U8((byte)bytes.Length);
        return Bytes(bytes);
    }

    public PacketWriter Bytes(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buf.AsSpan(Length));
        Length += data.Length;
        return this;
    }

    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();
}

public sealed class MalformedPacketException(string message) : Exception(message);

public ref struct PacketReader
{
    readonly ReadOnlySpan<byte> _data;
    int _pos;

    public PacketReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public int Remaining => _data.Length - _pos;

    ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || _pos + n > _data.Length) throw new MalformedPacketException("packet truncated");
        var s = _data.Slice(_pos, n);
        _pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];
    public bool Bool() => U8() != 0;
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int S32() => unchecked((int)U32());
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public Vector3 Vec() => new(F32(), F32(), F32());
    public string Str() => Encoding.UTF8.GetString(Take(U8()));
    public ReadOnlySpan<byte> Bytes(int n) => Take(n);
    public ReadOnlySpan<byte> Rest() => Take(Remaining);
}
