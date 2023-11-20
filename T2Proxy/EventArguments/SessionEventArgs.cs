using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Http.Responses;
using T2Proxy.Models;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.EventArguments;

public class SessionEventArgs : SessionEventArgsBase
{
    private bool disposed;

    private bool reRequest;

    private WebSocketDecoder? webSocketDecoderReceive;

    private WebSocketDecoder? webSocketDecoderSend;

    internal SessionEventArgs(ProxyServer server, ServerEndPoint endPoint, HttpClientStream clientStream, ConnectRequest? connectRequest, CancellationTokenSource cancellationTokenSource)
        : base(server, endPoint, clientStream, connectRequest, new Request(), cancellationTokenSource)
    {
    }

    public bool IsPromise { get; internal set; }

    private bool HasMulipartEventSubscribers => MultipartRequestPartSent != null;

    public bool ReRequest
    {
        get => reRequest;
        set
        {
            if (HttpClient.Response.StatusCode == 0) throw new Exception("Response status code is empty. Cannot request again a request " + "which was never send to server.");

            reRequest = value;
        }
    }

    [Obsolete("Use [WebSocketDecoderReceive] instead")]
    public WebSocketDecoder WebSocketDecoder => WebSocketDecoderReceive;

    public WebSocketDecoder WebSocketDecoderSend => webSocketDecoderSend ??= new WebSocketDecoder(BufferPool);

    public WebSocketDecoder WebSocketDecoderReceive => webSocketDecoderReceive ??= new WebSocketDecoder(BufferPool);

    public event EventHandler<MultipartRequestPartSentEventArgs>? MultipartRequestPartSent;

    private async Task ReadRequestBodyAsync(CancellationToken cancellationToken)
    {
        HttpClient.Request.EnsureBodyAvailable(false);

        var request = HttpClient.Request;

        if (!request.IsBodyRead)
        {
            if (request.IsBodyReceived) throw new Exception("Request body was already received.");

            if (request.HttpVersion == HttpHeader.Version20)
            {
                request.Http2IgnoreBodyFrames = true;

                request.Http2BodyData = new MemoryStream();

                var tcs = new TaskCompletionSource<bool>();
                request.ReadHttp2BodyTaskCompletionSource = tcs;

                request.ReadHttp2BeforeHandlerTaskCompletionSource!.SetResult(true);

                await tcs.Task;

                request.IsBodyRead = true;
                request.IsBodyReceived = true;
            }
            else
            {
                var body = await ReadBodyAsync(true, cancellationToken);
                if (!request.BodyAvailable) request.Body = body;

                request.IsBodyRead = true;
                request.IsBodyReceived = true;
            }
        }
    }
    internal async Task ClearResponse(CancellationToken cancellationToken)
    {
        await SyphonOutBodyAsync(false, cancellationToken);
        HttpClient.Response = new Response();
    }

    internal void OnMultipartRequestPartSent(ReadOnlySpan<char> boundary, HeaderCollection headers)
    {
        try
        {
            MultipartRequestPartSent?.Invoke(this, new MultipartRequestPartSentEventArgs(this, boundary.ToString(), headers));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }
    private async Task ReadResponseBodyAsync(CancellationToken cancellationToken)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot read the response body before request is made to server.");

        var response = HttpClient.Response;
        if (!response.HasBody) return;

        if (!response.IsBodyRead)
        {
            if (response.IsBodyReceived) throw new Exception("Response body was already received.");

            if (response.HttpVersion == HttpHeader.Version20)
            {
                response.Http2IgnoreBodyFrames = true;

                response.Http2BodyData = new MemoryStream();

                var tcs = new TaskCompletionSource<bool>();
                response.ReadHttp2BodyTaskCompletionSource = tcs;

                response.ReadHttp2BeforeHandlerTaskCompletionSource!.SetResult(true);

                await tcs.Task;

                response.IsBodyRead = true;
                response.IsBodyReceived = true;
            }
            else
            {
                var body = await ReadBodyAsync(false, cancellationToken);
                if (!response.BodyAvailable) response.Body = body;

                response.IsBodyRead = true;
                response.IsBodyReceived = true;
            }
        }
    }

    private async Task<byte[]> ReadBodyAsync(bool isRequest, CancellationToken cancellationToken)
    {
        using var bodyStream = new MemoryStream();
        using var writer = new HttpStream(Server, bodyStream, BufferPool, cancellationToken);

        if (isRequest)
            await CopyRequestBodyAsync(writer, TransformationMode.Uncompress, cancellationToken);
        else
            await CopyResponseBodyAsync(writer, TransformationMode.Uncompress, cancellationToken);

        return bodyStream.ToArray();
    }

    internal async Task SyphonOutBodyAsync(bool isRequest, CancellationToken cancellationToken)
    {
        var requestResponse = isRequest ? (RequestResponseBase)HttpClient.Request : HttpClient.Response;
        if (requestResponse.IsBodyReceived || !requestResponse.OriginalHasBody) return;

        var reader = isRequest ? (HttpStream)ClientStream : HttpClient.Connection.Stream;

        await reader.CopyBodyAsync(requestResponse, true, NullWriter.Instance, TransformationMode.None, isRequest, this, cancellationToken);
        requestResponse.IsBodyReceived = true;
    }

    internal async Task CopyRequestBodyAsync(IHttpStreamWriter writer, TransformationMode transformation, CancellationToken cancellationToken)
    {
        var request = HttpClient.Request;
        var reader = ClientStream;

        var contentLength = request.ContentLength;

        if (contentLength > 0 && HasMulipartEventSubscribers && request.IsMultipartFormData)
        {
            var boundary = HttpHelper.GetBoundaryFromContentType(request.ContentType);

            using (var copyStream = new CopyStream(reader, writer, BufferPool))
            {
                while (contentLength > copyStream.ReadBytes)
                {
                    var read = await ReadUntilBoundaryAsync(copyStream, contentLength, boundary, cancellationToken);
                    if (read == 0) break;

                    if (contentLength > copyStream.ReadBytes)
                    {
                        var headers = new HeaderCollection();
                        await HeaderParser.ReadHeaders(copyStream, headers, cancellationToken);
                        OnMultipartRequestPartSent(boundary.Span, headers);
                    }
                }

                await copyStream.FlushAsync(cancellationToken);
            }
        }
        else
        {
            await reader.CopyBodyAsync(request, false, writer, transformation, true, this, cancellationToken);
        }

        request.IsBodyReceived = true;
    }

    private async Task CopyResponseBodyAsync(IHttpStreamWriter writer, TransformationMode transformation, CancellationToken cancellationToken)
    {
        var response = HttpClient.Response;
        await HttpClient.Connection.Stream.CopyBodyAsync(response, false, writer, transformation, false, this, cancellationToken);
        response.IsBodyReceived = true;
    }

    private async Task<long> ReadUntilBoundaryAsync(ILineStream reader, long totalBytesToRead, ReadOnlyMemory<char> boundary, CancellationToken cancellationToken)
    {
        var bufferDataLength = 0;

        var buffer = BufferPool.GetBuffer();
        try
        {
            var boundaryLength = boundary.Length + 4;
            long bytesRead = 0;

            while (bytesRead < totalBytesToRead && (reader.DataAvailable || await reader.FillBufferAsync(cancellationToken)))
            {
                var newChar = reader.ReadByteFromBuffer();
                buffer[bufferDataLength] = newChar;

                bufferDataLength++;
                bytesRead++;

                if (bufferDataLength >= boundaryLength)
                {
                    var startIdx = bufferDataLength - boundaryLength;
                    if (buffer[startIdx] == '-' && buffer[startIdx + 1] == '-')
                    {
                        startIdx += 2;
                        var ok = true;
                        for (var i = 0; i < boundary.Length; i++)
                            if (buffer[startIdx + i] != boundary.Span[i])
                            {
                                ok = false;
                                break;
                            }

                        if (ok) break;
                    }
                }

                if (bufferDataLength == buffer.Length)
                {
                    const int bytesToKeep = 100;
                    Buffer.BlockCopy(buffer, buffer.Length - bytesToKeep, buffer, 0, bytesToKeep);
                    bufferDataLength = bytesToKeep;
                }
            }

            return bytesRead;
        }
        finally
        {
            BufferPool.ReturnBuffer(buffer);
        }
    }

    public async Task<byte[]> GetRequestBody(CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Request.IsBodyRead) await ReadRequestBodyAsync(cancellationToken);

        return HttpClient.Request.Body;
    }

    public async Task<string> GetRequestBodyAsString(CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Request.IsBodyRead) await ReadRequestBodyAsync(cancellationToken);

        return HttpClient.Request.BodyString;
    }

    public void SetRequestBody(byte[] body)
    {
        var request = HttpClient.Request;
        if (request.Locked) throw new Exception("You cannot call this function after request is made to server.");

        request.Body = body;
    }

    public void SetRequestBodyString(string body)
    {
        if (HttpClient.Request.Locked) throw new Exception("You cannot call this function after request is made to server.");

        SetRequestBody(HttpClient.Request.Encoding.GetBytes(body));
    }

    public async Task<byte[]> GetResponseBody(CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Response.IsBodyRead) await ReadResponseBodyAsync(cancellationToken);

        return HttpClient.Response.Body;
    }

    public async Task<string> GetResponseBodyAsString(CancellationToken cancellationToken = default)
    {
        if (!HttpClient.Response.IsBodyRead) await ReadResponseBodyAsync(cancellationToken);

        return HttpClient.Response.BodyString;
    }

    public void SetResponseBody(byte[] body)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot call this function before request is made to server.");

        var response = HttpClient.Response;
        response.Body = body;
    }

    public void SetResponseBodyString(string body)
    {
        if (!HttpClient.Request.Locked) throw new Exception("You cannot call this function before request is made to server.");

        var bodyBytes = HttpClient.Response.Encoding.GetBytes(body);

        SetResponseBody(bodyBytes);
    }

    public void Ok(string html, IDictionary<string, HttpHeader>? headers,
        bool closeServerConnection = false)
    {
        Ok(html, headers?.Values, closeServerConnection);
    }

    public void Ok(string html, IEnumerable<HttpHeader>? headers = null,
        bool closeServerConnection = false)
    {
        var response = new OkResponse();
        if (headers != null) response.Headers.AddHeaders(headers);

        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Body = response.Encoding.GetBytes(html ?? string.Empty);

        Respond(response, closeServerConnection);
    }

    public void Ok(byte[] result, IDictionary<string, HttpHeader>? headers,
        bool closeServerConnection = false)
    {
        Ok(result, headers?.Values, closeServerConnection);
    }

    public void Ok(byte[] result, IEnumerable<HttpHeader>? headers = null,
        bool closeServerConnection = false)
    {
        var response = new OkResponse();
        response.Headers.AddHeaders(headers);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Body = result;

        Respond(response, closeServerConnection);
    }

    public void GenericResponse(string html, HttpStatusCode status,
        IDictionary<string, HttpHeader>? headers, bool closeServerConnection = false)
    {
        GenericResponse(html, status, headers?.Values, closeServerConnection);
    }

    public void GenericResponse(string html, HttpStatusCode status,
        IEnumerable<HttpHeader>? headers = null, bool closeServerConnection = false)
    {
        var response = new GenericResponse(status);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeaders(headers);
        response.Body = response.Encoding.GetBytes(html ?? string.Empty);

        Respond(response, closeServerConnection);
    }

    public void GenericResponse(byte[] result, HttpStatusCode status,
        IDictionary<string, HttpHeader> headers, bool closeServerConnection = false)
    {
        GenericResponse(result, status, headers?.Values, closeServerConnection);
    }

    public void GenericResponse(byte[] result, HttpStatusCode status,
        IEnumerable<HttpHeader>? headers, bool closeServerConnection = false)
    {
        var response = new GenericResponse(status);
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeaders(headers);
        response.Body = result;

        Respond(response, closeServerConnection);
    }

    public void Redirect(string url, bool closeServerConnection = false)
    {
        var response = new RedirectResponse();
        response.HttpVersion = HttpClient.Request.HttpVersion;
        response.Headers.AddHeader(KnownHeaders.Location, url);
        response.Body = Array.Empty<byte>();

        Respond(response, closeServerConnection);
    }

    public void Respond(Response response, bool closeServerConnection = false)
    {
        if (HttpClient.Request.Locked)
        {
            if (HttpClient.Response.Locked) throw new Exception("You cannot call this function after response is sent to the client.");

            if (closeServerConnection)
                TerminateServerConnection();

            response.SetOriginalHeaders(HttpClient.Response);

            HttpClient.Response = response;
            HttpClient.Response.Locked = true;
        }
        else
        {
            HttpClient.Request.Locked = true;
            HttpClient.Request.CancelRequest = true;

            HttpClient.Response = response;
            HttpClient.Response.Locked = true;
        }
    }

    public void TerminateServerConnection()
    {
        HttpClient.CloseServerConnection = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed) return;

        MultipartRequestPartSent = null;
        disposed = true;

        base.Dispose(disposing);
    }

    ~SessionEventArgs()
    {
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}