using System.Buffers;

namespace T2Proxy.StreamExtended.BufferPool;

internal class DefaultBufferPool : IBufferPool
{
    public int BufferSize { get; set; } = 8192;

    public byte[] GetBuffer()
    {
        return ArrayPool<byte>.Shared.Rent(BufferSize);
    }

    public byte[] GetBuffer(int bufferSize)
    {
        return ArrayPool<byte>.Shared.Rent(bufferSize);
    }

    public void ReturnBuffer(byte[] buffer)
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public void Dispose()
    {
    }
}