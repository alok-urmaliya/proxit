using System;
using T2Proxy.Extensions;
using T2Proxy.Models;

namespace T2Proxy.Http;

public class KnownHeader
{
    public string String;
    internal ByteStream String8;

    private KnownHeader(string str)
    {
        String8 = (ByteStream)str;
        String = str;
    }

    public override string ToString()
    {
        return String;
    }

    internal bool Equals(ReadOnlySpan<char> value)
    {
        return String.AsSpan().EqualsIgnoreCase(value);
    }

    internal bool Equals(string? value)
    {
        return String.EqualsIgnoreCase(value);
    }

    public static implicit operator KnownHeader(string str)
    {
        return new(str);
    }
}