using System.IO;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Http;
using T2Proxy.Network.Tcp;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy.Helpers;

internal sealed class HttpClientStream : HttpStream
{
    internal HttpClientStream(ProxyServer server, TcpClientConnection connection, Stream stream, IBufferPool bufferPool,
        CancellationToken cancellationToken)
        : base(server, stream, bufferPool, cancellationToken)
    {
        Connection = connection;
    }

    public TcpClientConnection Connection { get; }

    internal async ValueTask WriteResponseAsync(Response response, CancellationToken cancellationToken = default)
    {
        var headerBuilder = new HeaderBuilder();

        headerBuilder.WriteResponseLine(response.HttpVersion, response.StatusCode, response.StatusDescription);

        await WriteAsync(response, headerBuilder, cancellationToken);
    }

    internal async ValueTask<RequestStatusInfo> ReadRequestLine(CancellationToken cancellationToken = default)
    {
        var httpCmd = await ReadLineAsync(cancellationToken);
        if (string.IsNullOrEmpty(httpCmd)) return default;

        Request.ParseRequestLine(httpCmd!, out var method, out var requestUri, out var version);

        return new RequestStatusInfo { Method = method, RequestUri = requestUri, Version = version };
    }
}