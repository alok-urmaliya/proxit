using System;
using System.Threading;
using System.Threading.Tasks;

namespace T2Proxy.StreamExtended.Network;

public interface IPeekStream
{
    byte PeekByteFromBuffer(int index);

    ValueTask<int> PeekByteAsync(int index, CancellationToken cancellationToken = default);

    ValueTask<int> PeekBytesAsync(byte[] buffer, int offset, int index, int count,
        CancellationToken cancellationToken = default);
}