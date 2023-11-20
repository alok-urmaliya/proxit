using System;
using T2Proxy.Models;

namespace T2Proxy.Extensions;

internal static class HttpHeaderExtensions
{
    internal static string GetString(this ByteStream str)
    {
        return GetString(str.Span);
    }

    internal static string GetString(this ReadOnlySpan<byte> bytes)
    {
#if NET6_0_OR_GREATER
        return HttpHeader.Encoding.GetString(bytes);
#else
        return HttpHeader.Encoding.GetString(bytes.ToArray());
#endif
    }

    internal static ByteStream GetByteString(this string str)
    {
        return HttpHeader.Encoding.GetBytes(str);
    }
}