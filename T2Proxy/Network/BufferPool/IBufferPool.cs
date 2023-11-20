using System;

namespace T2Proxy.StreamExtended.BufferPool;

public interface IBufferPool : IDisposable
{
    int BufferSize { get; }

    byte[] GetBuffer();

    byte[] GetBuffer(int bufferSize);

    void ReturnBuffer(byte[] buffer);
}