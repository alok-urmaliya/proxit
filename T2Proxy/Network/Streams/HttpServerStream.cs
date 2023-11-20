using System.IO;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Http;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy.Helpers;

internal sealed class HttpServerStream : HttpStream
{
    internal HttpServerStream(ProxyServer server, Stream stream, IBufferPool bufferPool,
        CancellationToken cancellationToken)
        : base(server, stream, bufferPool, cancellationToken)
    {
    }
    internal async ValueTask WriteRequestAsync(Request request, CancellationToken cancellationToken = default)
    {
        var headerBuilder = new HeaderBuilder();
        headerBuilder.WriteRequestLine(request.Method, request.RequestUriString, request.HttpVersion);
        await WriteAsync(request, headerBuilder, cancellationToken);
    }

    internal async ValueTask<ResponseStatusInfo> ReadResponseStatus(CancellationToken cancellationToken = default)
    {
        var httpStatus = await ReadLineAsync(cancellationToken) ??
                         throw new IOException("Invalid http status code.");

        if (httpStatus == string.Empty)
           
            httpStatus = await ReadLineAsync(cancellationToken) ??
                         throw new IOException("Response status is empty.");

        Response.ParseResponseLine(httpStatus, out var version, out var statusCode, out var description);
        return new ResponseStatusInfo { Version = version, StatusCode = statusCode, Description = description };
    }
}