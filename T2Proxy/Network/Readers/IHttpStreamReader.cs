﻿using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;

namespace T2Proxy.StreamExtended.Network;

public interface IHttpStreamReader : ILineStream
{
    int Read(byte[] buffer, int offset, int count);

    Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);

    Task CopyBodyAsync(IHttpStreamWriter writer, bool isChunked, long contentLength,
        bool isRequest, SessionEventArgs args, CancellationToken cancellationToken);
}