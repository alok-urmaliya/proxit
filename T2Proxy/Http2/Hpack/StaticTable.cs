

using System.Collections.Generic;
using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack;

internal static class StaticTable
{
    private static readonly List<HttpHeader> staticTable;

    private static readonly Dictionary<ByteStream, int> staticIndexByName;

    public static ByteStream KnownHeaderAuhtority = (ByteStream)":authority";

    public static ByteStream KnownHeaderMethod = (ByteStream)":method";

    public static ByteStream KnownHeaderPath = (ByteStream)":path";

    public static ByteStream KnownHeaderScheme = (ByteStream)":scheme";

    public static ByteStream KnownHeaderStatus = (ByteStream)":status";

    static StaticTable()
    {
        const int entryCount = 61;
        staticTable = new List<HttpHeader>(entryCount);
        staticIndexByName = new Dictionary<ByteStream, int>(entryCount);
        Create(KnownHeaderAuhtority, string.Empty);
        Create(KnownHeaderMethod, "GET"); 
        Create(KnownHeaderMethod, "POST");
        Create(KnownHeaderPath, "/");
        Create(KnownHeaderPath, "/index.html"); 
        Create(KnownHeaderScheme, "http"); 
        Create(KnownHeaderScheme, "https");
        Create(KnownHeaderStatus, "200"); 
        Create(KnownHeaderStatus, "204"); 
        Create(KnownHeaderStatus, "206"); 
        Create(KnownHeaderStatus, "304"); 
        Create(KnownHeaderStatus, "400"); 
        Create(KnownHeaderStatus, "404"); 
        Create(KnownHeaderStatus, "500"); 
        Create("Accept-Charset", string.Empty); 
        Create("Accept-Encoding", "gzip, deflate"); 
        Create("Accept-Language", string.Empty); 
        Create("Accept-Ranges", string.Empty); 
        Create("Accept", string.Empty); 
        Create("Access-Control-Allow-Origin", string.Empty); 
        Create("Age", string.Empty); 
        Create("Allow", string.Empty); 
        Create("Authorization", string.Empty); 
        Create("Cache-Control", string.Empty); 
        Create("Content-Disposition", string.Empty); 
        Create("Content-Encoding", string.Empty); 
        Create("Content-Language", string.Empty); 
        Create("Content-Length", string.Empty); 
        Create("Content-Location", string.Empty); 
        Create("Content-Range", string.Empty); 
        Create("Content-Type", string.Empty); 
        Create("Cookie", string.Empty); 
        Create("Date", string.Empty); 
        Create("ETag", string.Empty); 
        Create("Expect", string.Empty); 
        Create("Expires", string.Empty); 
        Create("From", string.Empty); 
        Create("Host", string.Empty); 
        Create("If-Match", string.Empty); 
        Create("If-Modified-Since", string.Empty); 
        Create("If-None-Match", string.Empty); 
        Create("If-Range", string.Empty); 
        Create("If-Unmodified-Since", string.Empty); 
        Create("Last-Modified", string.Empty); 
        Create("Link", string.Empty); 
        Create("Location", string.Empty); 
        Create("Max-Forwards", string.Empty); 
        Create("Proxy-Authenticate", string.Empty); 
        Create("Proxy-Authorization", string.Empty); 
        Create("Range", string.Empty); 
        Create("Referer", string.Empty); 
        Create("Refresh", string.Empty); 
        Create("Retry-After", string.Empty); 
        Create("Server", string.Empty); 
        Create("Set-Cookie", string.Empty); 
        Create("Strict-Transport-Security", string.Empty); 
        Create("Transfer-Encoding", string.Empty); 
        Create("User-Agent", string.Empty); 
        Create("Vary", string.Empty); 
        Create("Via", string.Empty); 
        Create("WWW-Authenticate", string.Empty); 
    }

    public static int Length => staticTable.Count;

    public static HttpHeader Get(int index)
    {
        return staticTable[index - 1];
    }

    public static int GetIndex(ByteStream name)
    {
        if (!staticIndexByName.TryGetValue(name, out var index)) return -1;

        return index;
    }

    public static int GetIndex(ByteStream name, ByteStream value)
    {
        var index = GetIndex(name);
        if (index == -1) return -1;
        while (index <= Length)
        {
            var entry = Get(index);
            if (!name.Equals(entry.NameData)) break;

            if (Equals(value, entry.Value)) return index;

            index++;
        }

        return -1;
    }

    private static void Create(string name, string value)
    {
        Create((ByteStream)name.ToLower(), value);
    }

    private static void Create(ByteStream name, string value)
    {
        staticTable.Add(new HttpHeader(name, (ByteStream)value));
        staticIndexByName[name] = staticTable.Count;
    }
}