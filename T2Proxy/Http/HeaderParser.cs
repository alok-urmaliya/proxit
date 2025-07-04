﻿using System;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.Http;

internal static class HeaderParser
{
    internal static async ValueTask ReadHeaders(ILineStream reader, HeaderCollection headerCollection,
        CancellationToken cancellationToken)
    {
        string? tmpLine;
        while (!string.IsNullOrEmpty(tmpLine = await reader.ReadLineAsync(cancellationToken)))
        {
            var colonIndex = tmpLine!.IndexOf(':');
            if (colonIndex == -1) throw new Exception("Header line should contain a colon character.");

            var headerName = tmpLine.AsSpan(0, colonIndex).ToString();
            var headerValue = tmpLine.AsSpan(colonIndex + 1).TrimStart().ToString();
            headerCollection.AddHeader(headerName, headerValue);
        }
    }
}