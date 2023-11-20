using System;
using T2Proxy.Models;

namespace T2Proxy.Extensions;

internal static class URIExtensions
{
    public static string GetOriginalPathAndQuery(this Uri uri)
    {
        var leftPart = uri.GetLeftPart(UriPartial.Authority);
        if (uri.OriginalString.StartsWith(leftPart))
            return uri.OriginalString.Substring(leftPart.Length);

        return uri.IsWellFormedOriginalString()
            ? uri.PathAndQuery
            : uri.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);
    }

    public static ByteStream GetScheme(ByteStream str)
    {
        if (str.Length < 3) return ByteStream.Empty;

        int i;

        for (i = 0; i < str.Length - 3; i++)
        {
            var ch = str[i];
            if (ch == ':') break;

            if (ch < 'A' || ch > 'z' || ch > 'Z' && ch < 'a') 
                return ByteStream.Empty;
        }

        if (str[i++] != ':') return ByteStream.Empty;

        if (str[i++] != '/') return ByteStream.Empty;

        if (str[i] != '/') return ByteStream.Empty;

        return new ByteStream(str.Data.Slice(0, i - 2));
    }
}