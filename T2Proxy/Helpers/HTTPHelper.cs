using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Extensions;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Shared;
using T2Proxy.StreamExtended.BufferPool;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.Helpers;

internal static class HttpHelper
{
   
    internal static Encoding GetEncodingFromContentType(string? contentType)
    {
        try
        {
            if (contentType == null) return HttpHeader.DefaultEncoding;

            foreach (var p in new SemicolonSplitEnumerator(contentType))
            {
                var parameter = p.Span;
                var equalsIndex = parameter.IndexOf('=');
                if (equalsIndex != -1 &&
                    KnownHeaders.ContentTypeCharset.Equals(parameter.Slice(0, equalsIndex).TrimStart()))
                {
                    var value = parameter.Slice(equalsIndex + 1);
                    if (value.EqualsIgnoreCase("x-user-defined".AsSpan())) continue;

                    if (value.Length > 2 && value[0] == '"' && value[value.Length - 1] == '"')
                        value = value.Slice(1, value.Length - 2);

                    return Encoding.GetEncoding(value.ToString());
                }
            }
        }
        catch
        {
           
        }
      
        return HttpHeader.DefaultEncoding;
    }

    internal static ReadOnlyMemory<char> GetBoundaryFromContentType(string? contentType)
    {
        if (contentType != null)
           
            foreach (var parameter in new SemicolonSplitEnumerator(contentType))
            {
                var equalsIndex = parameter.Span.IndexOf('=');
                if (equalsIndex != -1 &&
                    KnownHeaders.ContentTypeBoundary.Equals(parameter.Span.Slice(0, equalsIndex).TrimStart()))
                {
                    var value = parameter.Slice(equalsIndex + 1);
                    if (value.Length > 2 && value.Span[0] == '"' && value.Span[value.Length - 1] == '"')
                        value = value.Slice(1, value.Length - 2);

                    return value;
                }
            }


        return null;
    }

  
    internal static string GetWildCardDomainName(string hostname, bool disableWildCardCertificates)
    {
     

        if (IPAddress.TryParse(hostname, out _)) return hostname;

        if (disableWildCardCertificates) return hostname;

        var split = hostname.Split(ServerConstants.DotSplit);

        if (split.Length > 2)
        {
            if (split[0] != "www" && split[1].Length <= 3) return hostname;

            var idx = hostname.IndexOf(ServerConstants.DotSplit);

            if (hostname.Substring(0, idx).Contains("-")) return hostname;

            var rootDomain = hostname.Substring(idx + 1);
            return "*." + rootDomain;
        }

        
        return hostname;
    }

    public static async ValueTask<KnownMethod> GetMethod(IPeekStream httpReader, IBufferPool bufferPool,
        CancellationToken cancellationToken = default)
    {
        const int lengthToCheck = 20;
        if (bufferPool.BufferSize < lengthToCheck)
            throw new Exception($"Buffer is too small. Minimum size is {lengthToCheck} bytes");

        var buffer = bufferPool.GetBuffer(bufferPool.BufferSize);
        try
        {
            var i = 0;
            while (i < lengthToCheck)
            {
                var peeked = await httpReader.PeekBytesAsync(buffer, i, i, lengthToCheck - i, cancellationToken);
                if (peeked <= 0)
                    return KnownMethod.Invalid;

                peeked += i;

                while (i < peeked)
                {
                    int b = buffer[i];

                    if (b == ' ' && i > 2)
                        return GetKnownMethod(buffer.AsSpan(0, i));

                    var ch = (char)b;
                    if ((ch < 'A' || ch > 'z' || ch > 'Z' && ch < 'a') && ch != '-') // ASCII letter
                        return KnownMethod.Invalid;

                    i++;
                }
            }
            return KnownMethod.Invalid;
        }
        finally
        {
            bufferPool.ReturnBuffer(buffer);
        }
    }

    private static KnownMethod GetKnownMethod(ReadOnlySpan<byte> method)
    {
        var b1 = method[0];
        var b2 = method[1];
        var b3 = method[2];

        switch (method.Length)
        {
            case 3:
                if (b1 == 'G')
                    return b2 == 'E' && b3 == 'T' ? KnownMethod.Get : KnownMethod.Unknown;

                if (b1 == 'P')
                {
                    if (b2 == 'U')
                        return b3 == 'T' ? KnownMethod.Put : KnownMethod.Unknown;

                    if (b2 == 'R')
                        return b3 == 'I' ? KnownMethod.Pri : KnownMethod.Unknown;
                }

                break;
            case 4:
                if (b1 == 'H')
                    return b2 == 'E' && b3 == 'A' && method[3] == 'D' ? KnownMethod.Head : KnownMethod.Unknown;

                if (b1 == 'P')
                    return b2 == 'O' && b3 == 'S' && method[3] == 'T' ? KnownMethod.Post : KnownMethod.Unknown;

                break;
            case 5:
                if (b1 == 'T')
                    return b2 == 'R' && b3 == 'A' && method[3] == 'C' && method[4] == 'E'
                        ? KnownMethod.Trace
                        : KnownMethod.Unknown;

                break;
            case 6:
                if (b1 == 'D')
                    return b2 == 'E' && b3 == 'L' && method[3] == 'E' && method[4] == 'T' && method[5] == 'E'
                        ? KnownMethod.Delete
                        : KnownMethod.Unknown;

                break;
            case 7:
                if (b1 == 'C')
                    return b2 == 'O' && b3 == 'N' && method[3] == 'N' && method[4] == 'E' && method[5] == 'C' &&
                           method[6] == 'T'
                        ? KnownMethod.Connect
                        : KnownMethod.Unknown;

                if (b1 == 'O')
                    return b2 == 'P' && b3 == 'T' && method[3] == 'I' && method[4] == 'O' && method[5] == 'N' &&
                           method[6] == 'S'
                        ? KnownMethod.Options
                        : KnownMethod.Unknown;

                break;
        }


        return KnownMethod.Unknown;
    }

    private struct SemicolonSplitEnumerator
    {
        private readonly ReadOnlyMemory<char> data;

        private int idx;

        public SemicolonSplitEnumerator(string str) : this(str.AsMemory())
        {
        }

        public SemicolonSplitEnumerator(ReadOnlyMemory<char> data)
        {
            this.data = data;
            Current = null;
            idx = 0;
        }

        public SemicolonSplitEnumerator GetEnumerator()
        {
            return this;
        }

        public bool MoveNext()
        {
            if (this.idx > data.Length) return false;

            var idx = data.Span.Slice(this.idx).IndexOf(';');
            if (idx == -1)
                idx = data.Length;
            else
                idx += this.idx;

            Current = data.Slice(this.idx, idx - this.idx);
            this.idx = idx + 1;
            return true;
        }


        public ReadOnlyMemory<char> Current { get; private set; }
    }
}