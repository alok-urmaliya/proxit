#if NET6_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Compression;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Http;
using T2Proxy.Http2.Hpack;
using T2Proxy.Models;
using Decoder = T2Proxy.Http2.Hpack.Decoder;
using Encoder = T2Proxy.Http2.Hpack.Encoder;

namespace T2Proxy.Http2
{
    internal class Http2Helper
    {
        public static readonly byte[] ConnectionPreface = Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");

        internal static async Task SendHttp2(Stream clientStream, Stream serverStream,
            Func<SessionEventArgs> sessionFactory,
            Func<SessionEventArgs, Task> onBeforeRequest, Func<SessionEventArgs, Task> onBeforeResponse,
            CancellationTokenSource cancellationTokenSource, Guid connectionId,
            ExceptionHandler? exceptionFunc)
        {
            var clientSettings = new Http2Settings();
            var serverSettings = new Http2Settings();

            var sessions = new ConcurrentDictionary<int, SessionEventArgs>();

            var sendRelay =
                CopyHttp2FrameAsync(clientStream, serverStream, clientSettings, serverSettings,
                    sessionFactory, sessions, onBeforeRequest,
                    connectionId, true, cancellationTokenSource.Token, exceptionFunc);
            var receiveRelay =
                CopyHttp2FrameAsync(serverStream, clientStream, serverSettings, clientSettings,
                    sessionFactory, sessions, onBeforeResponse,
                    connectionId, false, cancellationTokenSource.Token, exceptionFunc);

            await Task.WhenAny(sendRelay, receiveRelay);
            cancellationTokenSource.Cancel();

            await Task.WhenAll(sendRelay, receiveRelay);
        }

        private static async Task CopyHttp2FrameAsync(Stream input, Stream output,
            Http2Settings localSettings, Http2Settings remoteSettings,
            Func<SessionEventArgs> sessionFactory, ConcurrentDictionary<int, SessionEventArgs> sessions,
            Func<SessionEventArgs, Task> onBeforeRequestResponse,
            Guid connectionId, bool isClient, CancellationToken cancellationToken,
            ExceptionHandler? exceptionFunc)
        {
            int headerTableSize = 0;
            Decoder? decoder = null;

            var frameHeader = new Http2FrameHeader();
            var frameHeaderBuffer = new byte[9];
            byte[]? buffer = null;
            while (true)
            {
                int read = await ForceRead(input, frameHeaderBuffer, 0, 9, cancellationToken);
                if (read != 9)
                {
                    return;
                }

                int length = (frameHeaderBuffer[0] << 16) + (frameHeaderBuffer[1] << 8) + frameHeaderBuffer[2];
                var type = (Http2FrameType)frameHeaderBuffer[3];
                var flags = (Http2FrameFlag)frameHeaderBuffer[4];
                int streamId = ((frameHeaderBuffer[5] & 0x7f) << 24) + (frameHeaderBuffer[6] << 16) +
                               (frameHeaderBuffer[7] << 8) + frameHeaderBuffer[8];

                frameHeader.Length = length;
                frameHeader.Type = type;
                frameHeader.Flags = flags;
                frameHeader.StreamId = streamId;

                if (buffer == null || buffer.Length < localSettings.MaxFrameSize)
                {
                    buffer = new byte[localSettings.MaxFrameSize];
                }

                read = await ForceRead(input, buffer, 0, length, cancellationToken);
                if (read != length)
                {
                    return;
                }

                bool sendPacket = true;
                bool endStream = false;

                SessionEventArgs? args = null;
                RequestResponseBase? rr = null;
                if (type == Http2FrameType.Data || type == Http2FrameType.Headers)
                {
                    if (!sessions.TryGetValue(streamId, out args))
                    {
                        if (type == Http2FrameType.PushPromise && isClient)
                        {
                            throw new ServerHttpException("HTTP Push promise received from the client.", null, args);
                        }
                    }
                }

               
                if (type == Http2FrameType.Data && args != null)
                {
                    if (isClient)
                        args.OnDataSent(buffer, 0, read);
                    else
                        args.OnDataReceived(buffer, 0, read);

                    rr = isClient ? (RequestResponseBase)args.HttpClient.Request : args.HttpClient.Response;

                    bool padded = (flags & Http2FrameFlag.Padded) != 0;
                    bool endStreamFlag = (flags & Http2FrameFlag.EndStream) != 0;
                    if (endStreamFlag)
                    {
                        endStream = true;
                    }

                    if (rr.Http2IgnoreBodyFrames)
                    {
                        sendPacket = false;
                    }

                    if (rr.ReadHttp2BodyTaskCompletionSource != null)
                    {
                      
                        var data = rr.Http2BodyData;
                        int offset = 0;
                        if (padded)
                        {
                            offset++;
                            length--;
                            length -= buffer[0];
                        }

                        data!.Write(buffer, offset, length);
                    }
                }
                else if (type == Http2FrameType.Headers)
                {
                    bool endHeaders = (flags & Http2FrameFlag.EndHeaders) != 0;
                    bool padded = (flags & Http2FrameFlag.Padded) != 0;
                    bool priority = (flags & Http2FrameFlag.Priority) != 0;
                    bool endStreamFlag = (flags & Http2FrameFlag.EndStream) != 0;
                    if (endStreamFlag)
                    {
                        endStream = true;
                    }

                    int offset = 0;
                    if (padded)
                    {
                        offset = 1;
                        Breakpoint();
                    }

                    if (type == Http2FrameType.PushPromise)
                    {
                        int promisedStreamId =
 (buffer[offset++] << 24) + (buffer[offset++] << 16) + (buffer[offset++] << 8) + buffer[offset++];
                        if (!sessions.TryGetValue(streamId, out args))
                        {
                            args = sessionFactory();
                            args.IsPromise = true;
                            if (!sessions.TryAdd(streamId, args))
                                ;
                            if (!sessions.TryAdd(promisedStreamId, args))
                                ;
                        }

                        System.Diagnostics.Debug.WriteLine("PROMISE STREAM: " + streamId + ", " + promisedStreamId +
                                                           ", CONN: " + connectionId);
                        rr = args.HttpClient.Request;

                        if (isClient)
                        {
                           
                            Breakpoint();
                        }
                    }
                    else
                    {
                        if (!sessions.TryGetValue(streamId, out args))
                        {
                            args = sessionFactory();
                            if (!sessions.TryAdd(streamId, args))
                                ;
                        }

                        rr = isClient ? (RequestResponseBase)args.HttpClient.Request : args.HttpClient.Response;
                        if (priority)
                        {
                            var priorityData = ((long)buffer[offset++] << 32) + ((long)buffer[offset++] << 24) +
                                               (buffer[offset++] << 16) + (buffer[offset++] << 8) + buffer[offset++];
                            rr.Priority = priorityData;
                        }
                    }


                    int dataLength = length - offset;
                    if (padded)
                    {
                        dataLength -= buffer[0];
                    }

                    var headerListener = new MyHeaderListener(
                        (name, value) =>
                        {
                            var headers = isClient ? args.HttpClient.Request.Headers : args.HttpClient.Response.Headers;
                            headers.AddHeader(new HttpHeader(name, value));
                        });
                    try
                    {
                        if (decoder == null || headerTableSize < localSettings.HeaderTableSize)
                        {
                            headerTableSize = localSettings.HeaderTableSize;
                            decoder = new Decoder(8192, headerTableSize);
                        }

                        decoder.Decode(new BinaryReader(new MemoryStream(buffer, offset, dataLength)),
                            headerListener);
                        decoder.EndHeaderBlock();

                        if (rr is Request request)
                        {
                            var method = headerListener.Method;
                            var path = headerListener.Path;
                            if (method.Length == 0 || path.Length == 0)
                            {
                                throw new Exception("HTTP/2 Missing method or path");
                            }

                            request.HttpVersion = HttpVersion.Version20;
                            request.Method = method.GetString();
                            request.IsHttps = headerListener.Scheme == ProxyServer.UriSchemeHttps;
                            request.Authority = headerListener.Authority;
                            request.RequestUriString8 = path;

                        }
                        else
                        {
                            var response = (Response)rr;
                            response.HttpVersion = HttpVersion.Version20;

                            string statusHack = HttpHeader.Encoding.GetString(headerListener.Status.Span);
                            int.TryParse(statusHack, out int statusCode);
                            response.StatusCode = statusCode;
                            response.StatusDescription = string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptionFunc?.Invoke(new ServerHttpException("Failed to decode HTTP/2 headers", ex, args));
                    }

                    if (!endHeaders)
                    {
                        Breakpoint();
                    }

                    if (endHeaders)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        rr.ReadHttp2BeforeHandlerTaskCompletionSource = tcs;

                        var handler = onBeforeRequestResponse(args);
                        rr.Http2BeforeHandlerTask = handler;

                        if (handler == await Task.WhenAny(tcs.Task, handler))
                        {
                            rr.ReadHttp2BeforeHandlerTaskCompletionSource = null;
                            tcs.SetResult(true);
                            await SendHeader(remoteSettings, frameHeader, frameHeaderBuffer, rr, endStream, output, args.IsPromise);
                        }
                        else
                        {
                            rr.Http2IgnoreBodyFrames = true;
                        }

                        rr.Locked = true;
                    }

                    sendPacket = false;
                }
                else if (type == Http2FrameType.Continuation)
                {
                    Breakpoint();
                }
                else if (type == Http2FrameType.Settings)
                {
                    if (length % 6 != 0)
                    {
                        throw new ServerHttpException("Invalid settings length", null, null);
                    }

                    int pos = 0;
                    while (pos < length)
                    {
                        int identifier = (buffer[pos++] << 8) + buffer[pos++];
                        int value =
 (buffer[pos++] << 24) + (buffer[pos++] << 16) + (buffer[pos++] << 8) + buffer[pos++];
                        if (identifier == 1 )
                        {
                            
                            remoteSettings.HeaderTableSize = value;
                        }
                        else if (identifier == 5)
                        {
                            remoteSettings.MaxFrameSize = value;
                        }
                    }
                }

                if (type == Http2FrameType.RstStream)
                {
                    int errorCode = (buffer[0] << 24) + (buffer[1] << 16) + (buffer[2] << 8) + buffer[3];
                    if (streamId == 0)
                    {
                      
                        exceptionFunc?.Invoke(new ServerHttpException("HTTP/2 connection error. Error code: " + errorCode, null, args));
                        return;
                    }
                    else
                    {
                       
                        sessions.TryRemove(streamId, out _);

                        if (errorCode != 8 )
                        {
                            exceptionFunc?.Invoke(new ServerHttpException("HTTP/2 stream error. Error code: " + errorCode, null, args));
                        }
                    }
                }

                if (endStream && rr!.ReadHttp2BodyTaskCompletionSource != null)
                {
                    if (!rr.BodyAvailable)
                    {
                        var data = rr.Http2BodyData;
                        var body = data!.ToArray();

                        if (rr.ContentEncoding != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                using (var zip =
                                    DecompressionFactory.Create(CompressionUtil.CompressionNameToEnum(rr.ContentEncoding), new MemoryStream(body)))
                                {
                                    zip.CopyTo(ms);
                                }

                                body = ms.ToArray();
                            }
                        }

                        if (!rr.BodyAvailable)
                        {
                            rr.Body = body;
                        }
                    }

                    rr.IsBodyRead = true;
                    rr.IsBodyReceived = true;

                    var tcs = rr.ReadHttp2BodyTaskCompletionSource;
                    rr.ReadHttp2BodyTaskCompletionSource = null;

                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetResult(true);
                    }

                    rr.Http2BodyData = null;

                    if (rr.Http2BeforeHandlerTask != null)
                    {
                        await rr.Http2BeforeHandlerTask;
                    }

                    if (args!.IsPromise)
                    {
                        Breakpoint();
                    }

                    await SendBody(remoteSettings, rr, frameHeader, frameHeaderBuffer, buffer, output);
                }

                if (!isClient && endStream)
                {
                    sessions.TryRemove(streamId, out _);
                    System.Diagnostics.Debug.WriteLine("REMOVED CONN: " + connectionId + ", CLIENT: " + isClient + ", STREAM: " + streamId + ", TYPE: " + type);
                }

                if (sendPacket)
                {
                    frameHeader.CopyToBuffer(frameHeaderBuffer);
                    await output.WriteAsync(frameHeaderBuffer, 0, frameHeaderBuffer.Length);
                    await output.WriteAsync(buffer, 0, length );
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

            }
        }

        [Conditional("DEBUG")]
        private static void Breakpoint()
        {
           
        }

        private static async Task SendHeader(Http2Settings settings, Http2FrameHeader frameHeader, byte[] frameHeaderBuffer, RequestResponseBase rr, bool endStream, Stream output, bool pushPromise)
        {
            var encoder = new Encoder(settings.HeaderTableSize);
            var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);
            if (rr.Priority.HasValue)
            {
                long p = rr.Priority.Value;
                writer.Write((byte)((p >> 32) & 0xff));
                writer.Write((byte)((p >> 24) & 0xff));
                writer.Write((byte)((p >> 16) & 0xff));
                writer.Write((byte)((p >> 8) & 0xff));
                writer.Write((byte)(p & 0xff));
            }

            if (rr is Request request)
            {
                var uri = request.RequestUri;
                encoder.EncodeHeader(writer, StaticTable.KnownHeaderMethod, request.Method.GetByteString());
                encoder.EncodeHeader(writer, StaticTable.KnownHeaderAuhtority, uri.Authority.GetByteString());
                encoder.EncodeHeader(writer, StaticTable.KnownHeaderScheme, uri.Scheme.GetByteString());
                encoder.EncodeHeader(writer, StaticTable.KnownHeaderPath, request.RequestUriString8, false,
                    HpackUtil.IndexType.None, false);
            }
            else
            {
                var response = (Response)rr;
                encoder.EncodeHeader(writer, StaticTable.KnownHeaderStatus, response.StatusCode.ToString().GetByteString());
            }

            foreach (var header in rr.Headers)
            {
                encoder.EncodeHeader(writer, header.NameData, header.ValueData);
            }

            var data = ms.ToArray();
            int newLength = data.Length;

            frameHeader.Length = newLength;
            frameHeader.Type = pushPromise ? Http2FrameType.PushPromise : Http2FrameType.Headers;

            var flags = Http2FrameFlag.EndHeaders;
            if (endStream)
            {
                flags |= Http2FrameFlag.EndStream;
            }

            if (rr.Priority.HasValue)
            {
                flags |= Http2FrameFlag.Priority;
            }

            frameHeader.Flags = flags;

            frameHeader.CopyToBuffer(frameHeaderBuffer);
            await output.WriteAsync(frameHeaderBuffer, 0, frameHeaderBuffer.Length/*, cancellationToken*/);
            await output.WriteAsync(data, 0, data.Length /*, cancellationToken*/);
        }

        private static async Task SendBody(Http2Settings settings, RequestResponseBase rr, Http2FrameHeader frameHeader, byte[] frameHeaderBuffer, byte[] buffer, Stream output)
        {
            var body = rr.CompressBodyAndUpdateContentLength();
            await SendHeader(settings, frameHeader, frameHeaderBuffer, rr, !(rr.HasBody && rr.IsBodyRead), output, false);

            if (rr.HasBody && rr.IsBodyRead)
            {
                int pos = 0;
                while (pos < body!.Length)
                {
                    int bodyFrameLength = Math.Min(buffer.Length, body.Length - pos);
                    Buffer.BlockCopy(body, pos, buffer, 0, bodyFrameLength);
                    pos += bodyFrameLength;

                    frameHeader.Length = bodyFrameLength;
                    frameHeader.Type = Http2FrameType.Data;
                    frameHeader.Flags = pos < body.Length ? (Http2FrameFlag)0 : Http2FrameFlag.EndStream;

                    frameHeader.CopyToBuffer(frameHeaderBuffer);
                    await output.WriteAsync(frameHeaderBuffer, 0, frameHeaderBuffer.Length/*, cancellationToken*/);
                    await output.WriteAsync(buffer, 0, bodyFrameLength /*, cancellationToken*/);
                }
            }
            else
            {
                ;
            }
        }

        private static async Task<int> ForceRead(Stream input, byte[] buffer, int offset, int bytesToRead,
            CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (bytesToRead > 0)
            {
                int read = await input.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                bytesToRead -= read;
                offset += read;
            }

            return totalRead;
        }


        class Http2Settings
        {
            public int HeaderTableSize { get; set; } = 4096;

            public int MaxFrameSize { get; set; } = 16384;
        }

        class MyHeaderListener : IHeaderListener
        {
            private readonly Action<ByteStream, ByteStream> addHeaderFunc;

            public ByteStream Method { get; private set; }

            public ByteStream Status { get; private set; }

            public ByteStream Authority { get; private set; }

            private ByteStream scheme;

            public ByteStream Path { get; private set; }

            public string Scheme
            {
                get
                {
                    if (scheme.Equals(ProxyServer.UriSchemeHttp8))
                    {
                        return ProxyServer.UriSchemeHttp;
                    }

                    if (scheme.Equals(ProxyServer.UriSchemeHttps8))
                    {
                        return ProxyServer.UriSchemeHttps;
                    }

                    return string.Empty;
                }
            }

            public MyHeaderListener(Action<ByteStream, ByteStream> addHeaderFunc)
            {
                this.addHeaderFunc = addHeaderFunc;
            }

            public void AddHeader(ByteStream name, ByteStream value, bool sensitive)
            {
                if (name.Span[0] == ':')
                {
                    string nameStr = Encoding.ASCII.GetString(name.Span);
                    switch (nameStr)
                    {
                        case ":method":
                            Method = value;
                            return;
                        case ":authority":
                            Authority = value;
                            return;
                        case ":scheme":
                            scheme = value;
                            return;
                        case ":path":
                            Path = value;
                            return;
                        case ":status":
                            Status = value;
                            return;
                    }
                }

                addHeaderFunc(name, value);
            }

            public Uri GetUri()
            {
                if (Authority.Length == 0)
                {
                  
                    Authority = HttpHeader.Encoding.GetBytes("abc.abc");
                }

                var bytes = new byte[scheme.Length + 3 + Authority.Length + Path.Length];
                scheme.Span.CopyTo(bytes);
                int idx = scheme.Length;
                bytes[idx++] = (byte)':';
                bytes[idx++] = (byte)'/';
                bytes[idx++] = (byte)'/';
                Authority.Span.CopyTo(bytes.AsSpan(idx, Authority.Length));
                idx += Authority.Length;
                Path.Span.CopyTo(bytes.AsSpan(idx, Path.Length));

                return new Uri(HttpHeader.Encoding.GetString(bytes));
            }
        }
    }
}
#endif