using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sarmat.TelemetryMonitor.Protocol;

internal ref struct MessagePackReader
{
    private readonly ReadOnlySpan<byte> data;
    private int offset;

    public MessagePackReader(ReadOnlySpan<byte> data) => this.data = data;

    public object? ReadValue()
    {
        var prefix = ReadByte();
        if (prefix <= 0x7f) return (long)prefix;
        if (prefix >= 0x90 && prefix <= 0x9f) return ReadArray(prefix & 0x0f);
        if (prefix >= 0xa0 && prefix <= 0xbf) return ReadString(prefix & 0x1f);
        if (prefix >= 0xe0) return (long)(sbyte)prefix;
        return prefix switch
        {
            0xc0 => null,
            0xc2 => false,
            0xc3 => true,
            0xca => ReadFloat32(),
            0xcb => ReadFloat64(),
            0xcc => (long)ReadByte(),
            0xcd => (long)ReadUInt16(),
            0xce => (long)ReadUInt32(),
            0xcf => checked((long)ReadUInt64()),
            0xd0 => (long)(sbyte)ReadByte(),
            0xd1 => (long)ReadInt16(),
            0xd2 => (long)ReadInt32(),
            0xd3 => ReadInt64(),
            0xd9 => ReadString(ReadByte()),
            0xda => ReadString(ReadUInt16()),
            0xdc => ReadArray(ReadUInt16()),
            _ => throw new InvalidDataException($"Unsupported MessagePack prefix 0x{prefix:X2}.")
        };
    }

    public void EnsureComplete()
    {
        if (offset != data.Length) throw new InvalidDataException("MessagePack frame contains trailing data.");
    }

    private object?[] ReadArray(int count)
    {
        var result = new object?[count];
        for (var i = 0; i < count; i++) result[i] = ReadValue();
        return result;
    }

    private string ReadString(int length) => Encoding.UTF8.GetString(ReadBytes(length));
    private float ReadFloat32() => BitConverter.Int32BitsToSingle(ReadInt32());
    private double ReadFloat64() => BitConverter.Int64BitsToDouble(ReadInt64());
    private ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));
    private uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));
    private ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8));
    private short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));
    private int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));
    private long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));

    private byte ReadByte()
    {
        if (offset >= data.Length) throw new EndOfStreamException("Unexpected end of MessagePack frame.");
        return data[offset++];
    }

    private ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0 || offset + length > data.Length)
            throw new EndOfStreamException("Unexpected end of MessagePack frame.");
        var result = data.Slice(offset, length);
        offset += length;
        return result;
    }
}
