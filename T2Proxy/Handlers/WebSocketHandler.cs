using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Network.Tcp;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task HandleWebSocketUpgrade(SessionEventArgs args,
        HttpClientStream clientStream, TcpServerConnection serverConnection,
        CancellationTokenSource cancellationTokenSource, CancellationToken cancellationToken)
    {
        await serverConnection.Stream.WriteRequestAsync(args.HttpClient.Request, cancellationToken);

        var httpStatus = await serverConnection.Stream.ReadResponseStatus(cancellationToken);

        var response = args.HttpClient.Response;
        response.HttpVersion = httpStatus.Version;
        response.StatusCode = httpStatus.StatusCode;
        response.StatusDescription = httpStatus.Description;

        await HeaderParser.ReadHeaders(serverConnection.Stream, response.Headers,
            cancellationToken);

        await clientStream.WriteResponseAsync(response, cancellationToken);

        if (!args.HttpClient.Response.Locked) await OnBeforeResponse(args);

        await TCPHelper.SendRaw(clientStream, serverConnection.Stream, BufferPool,
            args.OnDataSent, args.OnDataReceived, cancellationTokenSource, ExceptionFunc);
    }
}