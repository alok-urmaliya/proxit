using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Compression;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Shared;
using T2Proxy.StreamExtended.BufferPool;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.Helpers;

internal class HttpStream : Stream, IHttpStreamWriter, IHttpStreamReader, IPeekStream
{
    private readonly bool leaveOpen;
    private readonly byte[] streamBuffer;

    private static Encoding Encoding => HttpHeader.Encoding;

    private static readonly bool networkStreamHack = true;

    private int bufferPos;

    private bool disposed;

    private bool closedWrite;

    private readonly IBufferPool bufferPool;
    private readonly CancellationToken cancellationToken;

    public bool IsNetworkStream { get; }

    public event EventHandler<DataEventArgs>? DataRead;

    public event EventHandler<DataEventArgs>? DataWrite;

    private Stream BaseStream { get; }

    public bool IsClosed { get; private set; }

    static HttpStream()
    {
        try
        {
            var method = typeof(NetworkStream).GetMethod(nameof(Stream.ReadAsync),
                new[] { typeof(byte[]), typeof(int), typeof(int), typeof(CancellationToken) });
            if (method != null && method.DeclaringType != typeof(Stream)) networkStreamHack = false;
        }
        catch
        {
            // ignore
        }
    }

    private static readonly byte[] newLine = ServerConstants.NewLineBytes;
    private readonly ProxyServer server;

    internal HttpStream(ProxyServer server, Stream baseStream, IBufferPool bufferPool,
        CancellationToken cancellationToken, bool leaveOpen = false)
    {
        this.server = server;

        if (baseStream is NetworkStream) IsNetworkStream = true;

        BaseStream = baseStream;
        this.leaveOpen = leaveOpen;
        streamBuffer = bufferPool.GetBuffer();
        this.bufferPool = bufferPool;
        this.cancellationToken = cancellationToken;
    }
    public override void Flush()
    {
        if (closedWrite) return;

        try
        {
            BaseStream.Flush();
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        Available = 0;
        bufferPos = 0;
        return BaseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        BaseStream.SetLength(value);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Available == 0) FillBuffer();

        var available = Math.Min(Available, count);
        if (available > 0)
        {
            Buffer.BlockCopy(streamBuffer, bufferPos, buffer, offset, available);
            bufferPos += available;
            Available -= available;
        }

        return available;
    }
    [DebuggerStepThrough]
    public override void Write(byte[] buffer, int offset, int count)
    {
        OnDataWrite(buffer, offset, count);

        if (closedWrite) return;

        try
        {
            BaseStream.Write(buffer, offset, count);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        if (Available > 0)
        {
            await destination.WriteAsync(streamBuffer, bufferPos, Available, cancellationToken);

            Available = 0;
        }

        await base.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (closedWrite) return;

        try
        {
            await BaseStream.FlushAsync(cancellationToken);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (Available == 0) await FillBufferAsync(cancellationToken);

        var available = Math.Min(Available, count);
        if (available > 0)
        {
            Buffer.BlockCopy(streamBuffer, bufferPos, buffer, offset, available);
            bufferPos += available;
            Available -= available;
        }

        return available;
    }

#if NET6_0_OR_GREATER
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken =
 default)
#else
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
#endif
    {
        if (Available == 0) await FillBufferAsync(cancellationToken);

        var available = Math.Min(Available, buffer.Length);
        if (available > 0)
        {
            new Span<byte>(streamBuffer, bufferPos, available).CopyTo(buffer.Span);
            bufferPos += available;
            Available -= available;
        }

        return available;
    }

    public override int ReadByte()
    {
        if (Available == 0) FillBuffer();

        if (Available == 0) return -1;

        Available--;
        return streamBuffer[bufferPos++];
    }
    public async ValueTask<int> PeekByteAsync(int index, CancellationToken cancellationToken = default)
    {
        if (streamBuffer.Length <= index)
            throw new Exception("Requested Peek index exceeds the buffer size. Consider increasing the buffer size.");

        while (Available <= index)
        {
            var fillResult = await FillBufferAsync(cancellationToken);
            if (!fillResult) return -1;
        }

        return streamBuffer[bufferPos + index];
    }

    public async ValueTask<int> PeekBytesAsync(byte[] buffer, int offset, int index, int count,
        CancellationToken cancellationToken = default)
    {
        if (streamBuffer.Length <= index + count)
            throw new Exception(
                "Requested Peek index and size exceeds the buffer size. Consider increasing the buffer size.");

        while (Available <= index)
        {
            var fillResult = await FillBufferAsync(cancellationToken);
            if (!fillResult) return 0;
        }

        if (Available - index < count) count = Available - index;

        Buffer.BlockCopy(streamBuffer, index, buffer, offset, count);
        return count;
    }

    public byte PeekByteFromBuffer(int index)
    {
        if (Available <= index) throw new Exception("Index is out of buffer size");

        return streamBuffer[bufferPos + index];
    }

    public byte ReadByteFromBuffer()
    {
        if (Available == 0) throw new Exception("Buffer is empty");

        Available--;
        return streamBuffer[bufferPos++];
    }

    [DebuggerStepThrough]
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        OnDataWrite(buffer, offset, count);

        if (closedWrite) return;

        try
        {
            await BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }

    public override void WriteByte(byte value)
    {
        if (closedWrite) return;

        var buffer = bufferPool.GetBuffer();
        try
        {
            buffer[0] = value;
            OnDataWrite(buffer, 0, 1);
            BaseStream.Write(buffer, 0, 1);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
        finally
        {
            bufferPool.ReturnBuffer(buffer);
        }
    }

    protected virtual void OnDataWrite(byte[] buffer, int offset, int count)
    {
        DataWrite?.Invoke(this, new DataEventArgs(buffer, offset, count));
    }

    protected virtual void OnDataRead(byte[] buffer, int offset, int count)
    {
        DataRead?.Invoke(this, new DataEventArgs(buffer, offset, count));
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposed)
        {
            disposed = true;
            IsClosed = true;
            closedWrite = true;

            if (disposing)
            {
                if (!leaveOpen) BaseStream.Dispose();

                bufferPool.ReturnBuffer(streamBuffer);
            }
        }
    }

    public override bool CanRead => BaseStream.CanRead;

    public override bool CanSeek => BaseStream.CanSeek;

    public override bool CanWrite => BaseStream.CanWrite;

    public override bool CanTimeout => BaseStream.CanTimeout;

    public override long Length => BaseStream.Length;

    public bool DataAvailable => Available > 0;

    public int Available { get; private set; }

    public override long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }
    public override int ReadTimeout
    {
        get => BaseStream.ReadTimeout;
        set => BaseStream.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => BaseStream.WriteTimeout;
        set => BaseStream.WriteTimeout = value;
    }

    public bool FillBuffer()
    {
        if (IsClosed) throw new Exception("Stream is already closed");

        if (Available > 0)
            Buffer.BlockCopy(streamBuffer, bufferPos, streamBuffer, 0, Available);

        bufferPos = 0;

        var result = false;
        try
        {
            var readBytes = BaseStream.Read(streamBuffer, Available, streamBuffer.Length - Available);
            result = readBytes > 0;
            if (result)
            {
                OnDataRead(streamBuffer, Available, readBytes);
                Available += readBytes;
            }
        }
        catch
        {
            if (!IsNetworkStream)
                throw;
        }
        finally
        {
            if (!result)
            {
                IsClosed = true;
                closedWrite = true;
            }
        }

        return result;
    }

    public async ValueTask<bool> FillBufferAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosed) throw new Exception("Stream is already closed");

        var bytesToRead = streamBuffer.Length - Available;
        if (bytesToRead == 0) return false;

        if (Available > 0)
            Buffer.BlockCopy(streamBuffer, bufferPos, streamBuffer, 0, Available);

        bufferPos = 0;

        var result = false;
        try
        {
            var readTask = BaseStream.ReadAsync(streamBuffer, Available, bytesToRead, cancellationToken);
            if (IsNetworkStream) readTask = readTask.WithCancellation(cancellationToken);

            var readBytes = await readTask;
            result = readBytes > 0;
            if (result)
            {
                OnDataRead(streamBuffer, Available, readBytes);
                Available += readBytes;
            }
        }
        catch
        {
            if (!IsNetworkStream)
                throw;
        }
        finally
        {
            if (!result)
            {
                IsClosed = true;
                closedWrite = true;
            }
        }

        return result;
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        return ReadLineInternalAsync(this, bufferPool, cancellationToken);
    }

    internal static async ValueTask<string?> ReadLineInternalAsync(ILineStream reader, IBufferPool bufferPool,
        CancellationToken cancellationToken = default)
    {
        byte lastChar = default;

        var bufferDataLength = 0;
        var bufferPoolBuffer = bufferPool.GetBuffer();
        var buffer = bufferPoolBuffer;

        try
        {
            while (reader.DataAvailable || await reader.FillBufferAsync(cancellationToken))
            {
                var newChar = reader.ReadByteFromBuffer();
                buffer[bufferDataLength] = newChar;
                if (newChar == '\n')
                {
                    if (lastChar == '\r') return Encoding.GetString(buffer, 0, bufferDataLength - 1);

                    return Encoding.GetString(buffer, 0, bufferDataLength);
                }

                bufferDataLength++;

                lastChar = newChar;

                if (bufferDataLength == buffer.Length) Array.Resize(ref buffer, bufferDataLength * 2);
            }
        }
        finally
        {
            bufferPool.ReturnBuffer(bufferPoolBuffer);
        }

        if (bufferDataLength == 0) return null;

        return Encoding.GetString(buffer, 0, bufferDataLength);
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        if (!networkStreamHack) return base.BeginRead(buffer, offset, count, callback, state);

        var vAsyncResult = ReadAsync(buffer, offset, count, cancellationToken);
        if (IsNetworkStream) vAsyncResult = vAsyncResult.WithCancellation(cancellationToken);

        vAsyncResult.ContinueWith(pAsyncResult =>
        {
            callback?.Invoke(new TaskResult<int>(pAsyncResult, state));
        }, cancellationToken);

        return vAsyncResult;
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        if (!networkStreamHack) return base.EndRead(asyncResult);

        return ((TaskResult<int>)asyncResult).Result;
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        if (!networkStreamHack) return base.BeginWrite(buffer, offset, count, callback, state);

        var vAsyncResult = WriteAsync(buffer, offset, count, cancellationToken);

        vAsyncResult.ContinueWith(pAsyncResult => { callback?.Invoke(new TaskResult(pAsyncResult, state)); },
            cancellationToken);

        return vAsyncResult;
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        if (!networkStreamHack)
        {
            base.EndWrite(asyncResult);
            return;
        }

        ((TaskResult)asyncResult).GetResult();
    }

    public ValueTask WriteLineAsync(CancellationToken cancellationToken = default)
    {
        return WriteAsync(newLine, cancellationToken: cancellationToken);
    }

    private async ValueTask WriteAsyncInternal(string value, bool addNewLine, CancellationToken cancellationToken)
    {
        if (closedWrite) return;

        var newLineChars = addNewLine ? newLine.Length : 0;
        var charCount = value.Length;
        if (charCount < bufferPool.BufferSize - newLineChars)
        {
            var buffer = bufferPool.GetBuffer();
            try
            {
                var idx = Encoding.GetBytes(value, 0, charCount, buffer, 0);
                if (newLineChars > 0)
                {
                    Buffer.BlockCopy(newLine, 0, buffer, idx, newLineChars);
                    idx += newLineChars;
                }

                await BaseStream.WriteAsync(buffer, 0, idx, cancellationToken);
            }
            catch
            {
                closedWrite = true;
                if (!IsNetworkStream)
                    throw;
            }
            finally
            {
                bufferPool.ReturnBuffer(buffer);
            }
        }
        else
        {
            var buffer = new byte[charCount + newLineChars + 1];
            var idx = Encoding.GetBytes(value, 0, charCount, buffer, 0);
            if (newLineChars > 0)
            {
                Buffer.BlockCopy(newLine, 0, buffer, idx, newLineChars);
                idx += newLineChars;
            }

            try
            {
                await BaseStream.WriteAsync(buffer, 0, idx, cancellationToken);
            }
            catch
            {
                closedWrite = true;
                if (!IsNetworkStream)
                    throw;
            }
        }
    }

    public ValueTask WriteLineAsync(string value, CancellationToken cancellationToken = default)
    {
        return WriteAsyncInternal(value, true, cancellationToken);
    }
    internal async Task WriteHeadersAsync(HeaderBuilder headerBuilder, CancellationToken cancellationToken = default)
    {
        var buffer = headerBuilder.GetBuffer();

        try
        {
            await WriteAsync(buffer.Array, buffer.Offset, buffer.Count, true, cancellationToken);
        }
        catch (IOException e)
        {
            if (this is HttpServerStream)
                throw new RetryableServerConnectionException(
                    "Server connection was closed. Exception while sending request line and headers.", e);

            throw;
        }
    }

    internal async ValueTask WriteAsync(byte[] data, bool flush = false, CancellationToken cancellationToken = default)
    {
        if (closedWrite) return;

        try
        {
            await BaseStream.WriteAsync(data, 0, data.Length, cancellationToken);
            if (flush) await BaseStream.FlushAsync(cancellationToken);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }

    internal async Task WriteAsync(byte[] data, int offset, int count, bool flush,
        CancellationToken cancellationToken = default)
    {
        if (closedWrite) return;

        try
        {
            await BaseStream.WriteAsync(data, offset, count, cancellationToken);
            if (flush) await BaseStream.FlushAsync(cancellationToken);
        }
        catch
        {
            closedWrite = true;
            if (!IsNetworkStream)
                throw;
        }
    }

    internal ValueTask WriteBodyAsync(byte[] data, bool isChunked, CancellationToken cancellationToken)
    {
        if (isChunked) return WriteBodyChunkedAsync(data, cancellationToken);

        return WriteAsync(data, cancellationToken: cancellationToken);
    }

    public async Task CopyBodyAsync(RequestResponseBase requestResponse, bool useOriginalHeaderValues,
        IHttpStreamWriter writer, TransformationMode transformation, bool isRequest, SessionEventArgs args,
        CancellationToken cancellationToken)
    {
        var isChunked = useOriginalHeaderValues ? requestResponse.OriginalIsChunked : requestResponse.IsChunked;
        var contentLength = useOriginalHeaderValues
            ? requestResponse.OriginalContentLength
            : requestResponse.ContentLength;

        if (transformation == TransformationMode.None)
        {
            await CopyBodyAsync(writer, isChunked, contentLength, isRequest, args, cancellationToken);
            return;
        }

        LimitedStream limitedStream;
        Stream? decompressStream = null;

        var contentEncoding = useOriginalHeaderValues
            ? requestResponse.OriginalContentEncoding
            : requestResponse.ContentEncoding;

        Stream s = limitedStream = new LimitedStream(this, bufferPool, isChunked, contentLength);

        if (transformation == TransformationMode.Uncompress && contentEncoding != null)
            s = decompressStream =
                DecompressionFactory.Create(CompressionUtil.CompressionNameToEnum(contentEncoding), s);

        try
        {
            var http = new HttpStream(server, s, bufferPool, cancellationToken, true);
            await http.CopyBodyAsync(writer, false, -1, isRequest, args, cancellationToken);
        }
        finally
        {
            decompressStream?.Dispose();

            await limitedStream.Finish();
            limitedStream.Dispose();
        }
    }
    public Task CopyBodyAsync(IHttpStreamWriter writer, bool isChunked, long contentLength,
        bool isRequest,
        SessionEventArgs args, CancellationToken cancellationToken)
    {
#if DEBUG
            var isResponse = !isRequest;

            if (IsNetworkStream && writer.IsNetworkStream &&
                (isRequest && args.HttpClient.Request.OriginalHasBody && !args.HttpClient.Request.IsBodyRead && server.ShouldCallBeforeRequestBodyWrite()) ||
                (isResponse && args.HttpClient.Response.OriginalHasBody && !args.HttpClient.Response.IsBodyRead && server.ShouldCallBeforeResponseBodyWrite()))
            {
                return HandleBodyWrite(writer, isChunked, contentLength, isRequest, args, cancellationToken);
            }
#endif
        if (isChunked) return CopyBodyChunkedAsync(writer, isRequest, args, cancellationToken);

        if (contentLength == -1) contentLength = long.MaxValue;

        return CopyBytesToStream(writer, contentLength, isRequest, args, cancellationToken);
    }

    private Task HandleBodyWrite(IHttpStreamWriter writer, bool isChunked, long contentLength,
        bool isRequest, SessionEventArgs args, CancellationToken cancellationToken)
    {
        var originalContentLength = isRequest
            ? args.HttpClient.Request.OriginalContentLength
            : args.HttpClient.Response.OriginalContentLength;
        var originalIsChunked =
            isRequest ? args.HttpClient.Request.OriginalIsChunked : args.HttpClient.Response.OriginalIsChunked;

        throw new NotImplementedException();
    }
    private async ValueTask WriteBodyChunkedAsync(byte[] data, CancellationToken cancellationToken)
    {
        var chunkHead = Encoding.ASCII.GetBytes(data.Length.ToString("x2"));

        await WriteAsync(chunkHead, cancellationToken: cancellationToken);
        await WriteLineAsync(cancellationToken);
        await WriteAsync(data, cancellationToken: cancellationToken);
        await WriteLineAsync(cancellationToken);

        await WriteLineAsync("0", cancellationToken);
        await WriteLineAsync(cancellationToken);
    }

    private async Task CopyBodyChunkedAsync(IHttpStreamWriter writer, bool isRequest, SessionEventArgs args,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var chunkHead = await ReadLineAsync(cancellationToken);
            if (chunkHead == null) return;

            var idx = chunkHead.IndexOf(";", StringComparison.Ordinal);
            if (idx >= 0) chunkHead = chunkHead.Substring(0, idx);

            if (!int.TryParse(chunkHead, NumberStyles.HexNumber, null, out var chunkSize))
                throw new ServerHttpException($"Invalid chunk length: '{chunkHead}'", null, null);

            await writer.WriteLineAsync(chunkHead, cancellationToken);

            if (chunkSize != 0) await CopyBytesToStream(writer, chunkSize, isRequest, args, cancellationToken);

            await writer.WriteLineAsync(cancellationToken);

            // chunk trail
            await ReadLineAsync(cancellationToken);

            if (chunkSize == 0) break;
        }
    }

    private async Task CopyBytesToStream(IHttpStreamWriter writer, long count, bool isRequest, SessionEventArgs args,
        CancellationToken cancellationToken)
    {
        var buffer = bufferPool.GetBuffer();

        try
        {
            var remainingBytes = count;

            while (remainingBytes > 0)
            {
                var bytesToRead = buffer.Length;
                if (remainingBytes < bytesToRead) bytesToRead = (int)remainingBytes;

                var bytesRead = await ReadAsync(buffer, 0, bytesToRead, cancellationToken);
                if (bytesRead == 0) break;

                remainingBytes -= bytesRead;

                await writer.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                if (isRequest)
                    args.OnDataSent(buffer, 0, bytesRead);
                else
                    args.OnDataReceived(buffer, 0, bytesRead);
            }
        }
        finally
        {
            bufferPool.ReturnBuffer(buffer);
        }
    }
    protected async ValueTask WriteAsync(RequestResponseBase requestResponse, HeaderBuilder headerBuilder,
        CancellationToken cancellationToken = default)
    {
        var body = requestResponse.CompressBodyAndUpdateContentLength();
        headerBuilder.WriteHeaders(requestResponse.Headers);
        await WriteHeadersAsync(headerBuilder, cancellationToken);

        if (body != null)
        {
            await WriteBodyAsync(body, requestResponse.IsChunked, cancellationToken);
            requestResponse.IsBodySent = true;
        }
    }

#if NET6_0_OR_GREATER
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken =
 default)
        {
            if (closedWrite)
            {
                return;
            }

            try
            {
                await BaseStream.WriteAsync(buffer, cancellationToken);
            }
            catch
            {
                closedWrite = true;
                if (!IsNetworkStream)
                    throw;
            }
        }
#else
    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var buf = ArrayPool<byte>.Shared.Rent(buffer.Length);
        buffer.CopyTo(buf);
        try
        {
            await BaseStream.WriteAsync(buf, 0, buf.Length, cancellationToken);
        }
        catch
        {
            if (!IsNetworkStream)
                throw;
        }
    }
#endif
}