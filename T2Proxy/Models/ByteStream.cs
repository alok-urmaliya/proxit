using System;
using System.Text;
using T2Proxy.Extensions;

namespace T2Proxy.Models;

internal struct ByteStream : IEquatable<ByteStream>
{
    public static ByteStream Empty = new(ReadOnlyMemory<byte>.Empty);

    public ReadOnlyMemory<byte> Data { get; }

    public ReadOnlySpan<byte> Span => Data.Span;

    public int Length => Data.Length;

    public ByteStream(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    public override bool Equals(object? obj)
    {
        return obj is ByteStream other && Equals(other);
    }

    public bool Equals(ByteStream other)
    {
        return Data.Span.SequenceEqual(other.Data.Span);
    }

    public int IndexOf(byte value)
    {
        return Span.IndexOf(value);
    }

    public ByteStream Slice(int start)
    {
        return Data.Slice(start);
    }

    public ByteStream Slice(int start, int length)
    {
        return Data.Slice(start, length);
    }

    public override int GetHashCode()
    {
        return Data.GetHashCode();
    }

    public override string ToString()
    {
        return this.GetString();
    }

    public static explicit operator ByteStream(string str)
    {
        return new(Encoding.ASCII.GetBytes(str));
    }

    public static implicit operator ByteStream(byte[] data)
    {
        return new(data);
    }

    public static implicit operator ByteStream(ReadOnlyMemory<byte> data)
    {
        return new(data);
    }

    public byte this[int i] => Span[i];
}