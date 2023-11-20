using System;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Helpers;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy.StreamExtended.Network;

internal class CopyStream : ILineStream, IDisposable
{
    private readonly byte[] buffer;

    private readonly IBufferPool bufferPool;
    private readonly IHttpStreamReader reader;

    private readonly IHttpStreamWriter writer;

    private int bufferLength;

    private bool disposed;

    public CopyStream(IHttpStreamReader reader, IHttpStreamWriter writer, IBufferPool bufferPool)
    {
        this.reader = reader;
        this.writer = writer;
        buffer = bufferPool.GetBuffer();
        this.bufferPool = bufferPool;
    }

    public long ReadBytes { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public bool DataAvailable => reader.DataAvailable;

    public async ValueTask<bool> FillBufferAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        return await reader.FillBufferAsync(cancellationToken);
    }

    public byte ReadByteFromBuffer()
    {
        var b = reader.ReadByteFromBuffer();
        buffer[bufferLength++] = b;
        ReadBytes++;
        return b;
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        return HttpStream.ReadLineInternalAsync(this, bufferPool, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (bufferLength > 0)
        {
            await writer.WriteAsync(buffer, 0, bufferLength, cancellationToken);
            bufferLength = 0;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing) bufferPool.ReturnBuffer(buffer);

        disposed = true;
    }

    ~CopyStream()
    {
#if DEBUG
            
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}