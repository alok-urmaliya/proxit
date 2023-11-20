using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Network;
using T2Proxy.Network.Tcp;
using T2Proxy.Shared;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task HandleHttpSessionRequest(ServerEndPoint endPoint, HttpClientStream clientStream,
        CancellationTokenSource cancellationTokenSource, TunnelConnectSessionEventArgs? connectArgs = null,
        Task<TcpServerConnection?>? prefetchConnectionTask = null, bool isHttps = false)
    {
        var connectRequest = connectArgs?.HttpClient.ConnectRequest;
        var prefetchTask = prefetchConnectionTask;
        TcpServerConnection? connection = null;
        var closeServerConnection = false;
        try
        {
            var cancellationToken = cancellationTokenSource.Token;
            while (true)
            {
                if (clientStream.IsClosed) return;
                var requestLine = await clientStream.ReadRequestLine(cancellationToken);
                if (requestLine.IsEmpty()) return;
                var args = new SessionEventArgs(this, endPoint, clientStream, connectRequest, cancellationTokenSource)
                {
                    UserData = connectArgs?.UserData
                };

                var request = args.HttpClient.Request;
                if (isHttps) request.IsHttps = true;
                try
                {
                    try
                    {
                        await HeaderParser.ReadHeaders(clientStream, args.HttpClient.Request.Headers,
                            cancellationToken);

                        if (connectRequest != null)
                        {
                            request.IsHttps = connectRequest.IsHttps;
                            request.Authority = connectRequest.Authority;
                        }
                        request.RequestUriString8 = requestLine.RequestUri;
                        request.Method = requestLine.Method;
                        request.HttpVersion = requestLine.Version;
                        request.SetOriginalHeaders();
                        await OnBeforeRequest(args);
                        if (!args.IsTransparent && !args.IsSocks)
                        {
                            if (connectRequest == null && await CheckAuthorization(args) == false)
                            {
                                await OnBeforeResponse(args);
                                await clientStream.WriteResponseAsync(args.HttpClient.Response, cancellationToken);
                                return;
                            }

                            PrepareRequestHeaders(request.Headers);
                            request.Host = request.RequestUri.Authority;
                        }
                        if (args.EnableWinAuth && request.HasBody) await args.GetRequestBody(cancellationToken);

                        var response = args.HttpClient.Response;

                        if (request.CancelRequest)
                        {
                            if (!(Enable100ContinueBehaviour && request.ExpectContinue))
                                await args.SyphonOutBodyAsync(true, cancellationToken);
                            await HandleHttpSessionResponse(args);
                            if (!response.KeepAlive) return;
                            continue;
                        }
                        if (connection == null && prefetchTask != null)
                        {
                            try
                            {
                                connection = await prefetchTask;
                            }
                            catch (SocketException e)
                            {
                                if (e.SocketErrorCode != SocketError.HostNotFound) throw;
                            }
                            prefetchTask = null;
                        }

                        if (connection != null)
                        {
                            var socket = connection.TcpSocket;
                            var part1 = socket.Poll(1000, SelectMode.SelectRead);
                            var part2 = socket.Available == 0;
                            if (part1 & part2)
                            {
                                await TcpConnectionFactory.Release(connection, true);
                                connection = null;
                            }
                        }
                        if (connection != null
                            && await TcpConnectionFactory.GetConnectionCacheKey(this, args,
                                clientStream.Connection.NegotiatedApplicationProtocol)
                            != connection.CacheKey)
                        {
                            await TcpConnectionFactory.Release(connection);
                            connection = null;
                        }
                        var result = await HandleHttpSessionRequest(args, connection,
                            clientStream.Connection.NegotiatedApplicationProtocol,
                            cancellationToken, cancellationTokenSource);
                        var newConnection = result.LatestConnection;
                        if (connection != newConnection && connection != null)
                            await TcpConnectionFactory.Release(connection);
                        connection = result.LatestConnection;

                        closeServerConnection = !result.Continue;

                        if (result.Exception != null) throw result.Exception;

                        if (!result.Continue) return;
                        if (args.HttpClient.CloseServerConnection)
                        {
                            closeServerConnection = true;
                            return;
                        }

                        if (!response.KeepAlive)
                        {
                            closeServerConnection = true;
                            return;
                        }

                        if (cancellationTokenSource.IsCancellationRequested)
                            throw new Exception("Session was terminated by user.");

                        if (EnableConnectionPool && connection != null
                                                 && !connection.IsWinAuthenticated)
                        {
                            await TcpConnectionFactory.Release(connection);
                            connection = null;
                        }
                    }
                    catch (Exception e) when (!(e is ServerHttpException))
                    {
                        throw new ServerHttpException("Error occured whilst handling session request", e, args);
                    }
                }
                catch (Exception e)
                {
                    args.Exception = e;
                    closeServerConnection = true;
                    throw;
                }
                finally
                {
                    await OnAfterResponse(args);
                    args.Dispose();
                }
            }
        }
        finally
        {
            if (connection != null) await TcpConnectionFactory.Release(connection, closeServerConnection);
            await TcpConnectionFactory.Release(prefetchTask, closeServerConnection);
        }
    }

    private async Task<RetryResult> HandleHttpSessionRequest(SessionEventArgs args,
        TcpServerConnection? serverConnection, SslApplicationProtocol sslApplicationProtocol,
        CancellationToken cancellationToken, CancellationTokenSource cancellationTokenSource)
    {
        args.HttpClient.Request.Locked = true;

        var noCache = args.HttpClient.Request.UpgradeToWebSocket;

        if (noCache) serverConnection = null;

        var generator = () =>
            TcpConnectionFactory.GetServerConnection(this,
                args,
                false,
                sslApplicationProtocol,
                noCache,
                cancellationToken);
        return await RetryPolicy<RetryableServerConnectionException>().ExecuteAsync(async connection =>
        {
            args.HttpClient.SetConnection(connection);

            args.TimeLine["Connection Ready"] = DateTime.UtcNow;

            if (args.HttpClient.Request.UpgradeToWebSocket)
            {
                if (args.HttpClient.ConnectRequest != null)
                    args.HttpClient.ConnectRequest!.TunnelType = TunnelType.Websocket;

                await HandleWebSocketUpgrade(args, args.ClientStream, connection, cancellationTokenSource,
                    cancellationToken);
                return false;
            }
            await HandleHttpSessionRequest(args);
            return true;
        }, generator, serverConnection);
    }

    private async Task HandleHttpSessionRequest(SessionEventArgs args)
    {
        var cancellationToken = args.CancellationTokenSource.Token;
        var request = args.HttpClient.Request;

        var body = request.CompressBodyAndUpdateContentLength();

        await args.HttpClient.SendRequest(Enable100ContinueBehaviour, args.IsTransparent,
            cancellationToken);

        if (request.ExpectationSucceeded)
        {
            var writer = args.ClientStream;
            var response = args.HttpClient.Response;

            var headerBuilder = new HeaderBuilder();
            headerBuilder.WriteResponseLine(response.HttpVersion, response.StatusCode, response.StatusDescription);
            headerBuilder.WriteHeaders(response.Headers);
            await writer.WriteHeadersAsync(headerBuilder, cancellationToken);

            await args.ClearResponse(cancellationToken);
        }

        if (request.HasBody)
        {
            if (request.IsBodyRead)
                await args.HttpClient.Connection.Stream.WriteBodyAsync(body!, request.IsChunked, cancellationToken);
            else if (!request.ExpectationFailed)
                await args.CopyRequestBodyAsync(args.HttpClient.Connection.Stream, TransformationMode.None,
                    cancellationToken);
        }

        args.TimeLine["Request Sent"] = DateTime.UtcNow;
        await HandleHttpSessionResponse(args);
    }

    private void PrepareRequestHeaders(HeaderCollection requestHeaders)
    {
        var acceptEncoding = requestHeaders.GetHeaderValueOrNull(KnownHeaders.AcceptEncoding);

        if (acceptEncoding != null)
        {
            var supportedAcceptEncoding = new List<string>();
            supportedAcceptEncoding.AddRange(acceptEncoding.Split(',')
                .Select(x => x.Trim())
                .Where(x => ServerConstants.ProxySupportedCompressions.Contains(x)));

            supportedAcceptEncoding.Add("identity");

            requestHeaders.SetOrAddHeaderValue(KnownHeaders.AcceptEncoding,
                string.Join(", ", supportedAcceptEncoding));
        }

        requestHeaders.FixProxyHeaders();
    }

    private async Task OnBeforeRequest(SessionEventArgs args)
    {
        args.TimeLine["Request Received"] = DateTime.UtcNow;

        if (BeforeRequest != null) await BeforeRequest.InvokeAsync(this, args, ExceptionFunc);
    }
    internal async Task OnBeforeUpStreamConnectRequest(ConnectRequest request)
    {
        if (BeforeUpStreamConnectRequest != null)
            await BeforeUpStreamConnectRequest.InvokeAsync(this, request, ExceptionFunc);
    }

#if DEBUG
    internal bool ShouldCallBeforeRequestBodyWrite()
    {
        if (OnRequestBodyWrite != null)
        {
            return true;
        }

        return false;
    }

    internal async Task OnBeforeRequestBodyWrite(BeforeBodyWriteEventArgs args)
    {
        if (OnRequestBodyWrite != null)
        {
            await OnRequestBodyWrite.InvokeAsync(this, args, ExceptionFunc);
        }
    }
#endif
}