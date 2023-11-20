using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Http2;
using T2Proxy.Models;
using T2Proxy.Network.Tcp;
using T2Proxy.StreamExtended;
using SslExtensions = T2Proxy.Extensions.SSLExtensions;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task HandleClient(ExplicitServerEndPoint endPoint, TcpClientConnection clientConnection)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var clientStream = new HttpClientStream(this, clientConnection, clientConnection.GetStream(), BufferPool,
            cancellationToken);

        Task<TcpServerConnection?>? prefetchConnectionTask = null;
        var closeServerConnection = false;

        TunnelConnectSessionEventArgs? connectArgs = null;

        try
        {
            var method = await HttpHelper.GetMethod(clientStream, BufferPool, cancellationToken);
            if (clientStream.IsClosed) return;

            if (method == KnownMethod.Connect)
            {
                var requestLine = await clientStream.ReadRequestLine(cancellationToken);
                if (requestLine.IsEmpty()) return;

                var connectRequest = new ConnectRequest(requestLine.RequestUri)
                {
                    RequestUriString8 = requestLine.RequestUri,
                    HttpVersion = requestLine.Version
                };

                await HeaderParser.ReadHeaders(clientStream, connectRequest.Headers, cancellationToken);

                connectArgs = new TunnelConnectSessionEventArgs(this, endPoint, connectRequest, clientStream,
                    cancellationTokenSource);
                clientStream.DataRead += (o, args) => connectArgs.OnDataSent(args.Buffer, args.Offset, args.Count);
                clientStream.DataWrite += (o, args) => connectArgs.OnDataReceived(args.Buffer, args.Offset, args.Count);

                await endPoint.InvokeBeforeTunnelConnectRequest(this, connectArgs, ExceptionFunc);

                var decryptSsl = endPoint.DecryptSsl && connectArgs.DecryptSsl;
                var sendRawData = !decryptSsl;

                if (connectArgs.DenyConnect)
                {
                    if (connectArgs.HttpClient.Response.StatusCode == 0)
                        connectArgs.HttpClient.Response = new Response
                        {
                            HttpVersion = HttpHeader.Version11,
                            StatusCode = (int)HttpStatusCode.Forbidden,
                            StatusDescription = "Forbidden"
                        };

                    await clientStream.WriteResponseAsync(connectArgs.HttpClient.Response, cancellationToken);
                    return;
                }

                if (await CheckAuthorization(connectArgs) == false)
                {
                    await endPoint.InvokeBeforeTunnelConnectResponse(this, connectArgs, ExceptionFunc);

                    await clientStream.WriteResponseAsync(connectArgs.HttpClient.Response, cancellationToken);
                    return;
                }

                var response = ConnectResponse.CreateSuccessfulConnectResponse(connectRequest.HttpVersion);

                response.ContentLength = 0;
                response.Headers.FixProxyHeaders();
                connectArgs.HttpClient.Response = response;

                await clientStream.WriteResponseAsync(response, cancellationToken);

                var clientHelloInfo = await SslTools.PeekClientHello(clientStream, BufferPool, cancellationToken);
                if (clientStream.IsClosed) return;

                var isClientHello = clientHelloInfo != null;
                if (clientHelloInfo != null)
                {
                    connectRequest.TunnelType = TunnelType.Https;
                    connectRequest.ClientHelloInfo = clientHelloInfo;
                }

                await endPoint.InvokeBeforeTunnelConnectResponse(this, connectArgs, ExceptionFunc, isClientHello);

                if (decryptSsl && clientHelloInfo != null)
                {
                    connectRequest.IsHttps = true; 

                    var sslProtocol = clientHelloInfo.SslProtocol & SupportedSslProtocols;
                    if (sslProtocol == SslProtocols.None)
                    {
                        throw new Exception("Unsupported client SSL version.");
                    }

                    clientStream.Connection.SslProtocol = sslProtocol;

                    var http2Supported = false;

                    if (EnableHttp2)
                    {
                        var alpn = clientHelloInfo.GetAlpn();
                        if (alpn != null && alpn.Contains(SslApplicationProtocol.Http2))
                     
                            try
                            {
                                var connection = await TcpConnectionFactory.GetServerConnection(this, connectArgs,
                                    true, SslExtensions.Http2ProtocolAsList,
                                    true, true, cancellationToken);

                                if (connection != null)
                                {
                                    http2Supported = connection.NegotiatedApplicationProtocol ==
                                                     SslApplicationProtocol.Http2;

                                    await TcpConnectionFactory.Release(connection, true);
                                }
                            }
                            catch (Exception)
                            {
                                // ignore
                            }
                    }

                    if (EnableTcpServerConnectionPrefetch)
                        prefetchConnectionTask = TcpConnectionFactory.GetServerConnection(this, connectArgs,
                            true, null, false, true,
                            CancellationToken.None);

                    var connectHostname = requestLine.RequestUri.GetString();
                    var idx = connectHostname.IndexOf(":");
                    if (idx >= 0) connectHostname = connectHostname.Substring(0, idx);

                    X509Certificate2? certificate = null;
                    SslStream? sslStream = null;
                    try
                    {
                        sslStream = new SslStream(clientStream, false);

                        var certName = HttpHelper.GetWildCardDomainName(connectHostname,
                            CertificateManager.DisableWildCardCertificates);
                        certificate = endPoint.GenericCertificate ??
                                      await CertificateManager.CreateServerCertificate(certName);

                        var options = new SslServerAuthenticationOptions();
                        if (EnableHttp2 && http2Supported)
                        {
                            options.ApplicationProtocols = clientHelloInfo.GetAlpn();
                            if (options.ApplicationProtocols == null || options.ApplicationProtocols.Count == 0)
                                options.ApplicationProtocols = SslExtensions.Http11ProtocolAsList;
                        }

                        options.ServerCertificate = certificate;
                        options.ClientCertificateRequired = false;
                        options.EnabledSslProtocols = SupportedSslProtocols;
                        options.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
                        await sslStream.AuthenticateAsServerAsync(options, cancellationToken);

#if NET6_0_OR_GREATER
                            clientStream.Connection.NegotiatedApplicationProtocol =
 sslStream.NegotiatedApplicationProtocol;
#endif
                        clientStream = new HttpClientStream(this, clientStream.Connection, sslStream, BufferPool,
                            cancellationToken);
                        sslStream = null;

                        clientStream.DataRead += (o, args) =>
                            connectArgs.OnDecryptedDataSent(args.Buffer, args.Offset, args.Count);
                        clientStream.DataWrite += (o, args) =>
                            connectArgs.OnDecryptedDataReceived(args.Buffer, args.Offset, args.Count);
                    }
                    catch (Exception e)
                    {
                        sslStream?.Dispose();

                        var certName = certificate?.GetNameInfo(X509NameType.SimpleName, false);
                        throw new ServerConnectException(
                            $"Couldn't authenticate host '{connectHostname}' with certificate '{certName}'.", e,
                            connectArgs);
                    }

                    method = await HttpHelper.GetMethod(clientStream, BufferPool, cancellationToken);
                    if (clientStream.IsClosed) return;

                    if (method == KnownMethod.Invalid)
                    {
                        sendRawData = true;
                        await TcpConnectionFactory.Release(prefetchConnectionTask, true);
                        prefetchConnectionTask = null;
                    }
                }
                else if (clientHelloInfo == null)
                {
                    method = await HttpHelper.GetMethod(clientStream, BufferPool, cancellationToken);
                    if (clientStream.IsClosed) return;
                }

                if (cancellationTokenSource.IsCancellationRequested)
                    throw new Exception("Session was terminated by user.");

                if (method == KnownMethod.Invalid) sendRawData = true;

                if (sendRawData)
                {
                    var connection = (await TcpConnectionFactory.GetServerConnection(this, connectArgs,
                        true, null,
                        true, false, cancellationToken))!;

                    try
                    {
                        if (isClientHello)
                        {
                            var available = clientStream.Available;
                            if (available > 0)
                            {
                                var data = BufferPool.GetBuffer();

                                try
                                {
                                    var read = await clientStream.ReadAsync(data, 0, available, cancellationToken);
                                    if (read != available) throw new Exception("Internal error.");

                                    await connection.Stream.WriteAsync(data, 0, available, true, cancellationToken);
                                }
                                finally
                                {
                                    BufferPool.ReturnBuffer(data);
                                }
                            }

                            var serverHelloInfo =
                                await SslTools.PeekServerHello(connection.Stream, BufferPool, cancellationToken);
                            ((ConnectResponse)connectArgs.HttpClient.Response).ServerHelloInfo = serverHelloInfo;
                        }

                        if (!clientStream.IsClosed && !connection.Stream.IsClosed)
                            await TCPHelper.SendRaw(clientStream, connection.Stream, BufferPool,
                                null, null, connectArgs.CancellationTokenSource, ExceptionFunc);
                    }
                    finally
                    {
                        await TcpConnectionFactory.Release(connection, true);
                    }

                    return;
                }
            }

            if (connectArgs != null && method == KnownMethod.Pri)
            {
                var httpCmd = await clientStream.ReadLineAsync(cancellationToken);
                if (httpCmd == "PRI * HTTP/2.0")
                {
                    connectArgs.HttpClient.ConnectRequest!.TunnelType = TunnelType.Http2;

                    var line = await clientStream.ReadLineAsync(cancellationToken);
                    if (line != string.Empty)
                        throw new Exception($"HTTP/2 Protocol violation. Empty string expected, '{line}' received");

                    line = await clientStream.ReadLineAsync(cancellationToken);
                    if (line != "SM")
                        throw new Exception($"HTTP/2 Protocol violation. 'SM' expected, '{line}' received");

                    line = await clientStream.ReadLineAsync(cancellationToken);
                    if (line != string.Empty)
                        throw new Exception($"HTTP/2 Protocol violation. Empty string expected, '{line}' received");

                    var connection = (await TcpConnectionFactory.GetServerConnection(this, connectArgs,
                        true, SslExtensions.Http2ProtocolAsList,
                        true, false, cancellationToken))!;
                    try
                    {
#if NET6_0_OR_GREATER
                            var connectionPreface = new ReadOnlyMemory<byte>(Http2Helper.ConnectionPreface);
                            await connection.Stream.WriteAsync(connectionPreface, cancellationToken);
                            await Http2Helper.SendHttp2(clientStream, connection.Stream,
                                () => new SessionEventArgs(this, endPoint, clientStream, connectArgs?.HttpClient.ConnectRequest, cancellationTokenSource)
                                {
                                    UserData = connectArgs?.UserData
                                },
                                async args => { await OnBeforeRequest(args); },
                                async args => { await OnBeforeResponse(args); },
                                connectArgs.CancellationTokenSource, clientStream.Connection.Id, ExceptionFunc);
#endif
                    }
                    finally
                    {
                        await TcpConnectionFactory.Release(connection, true);
                    }
                }
            }

            var prefetchTask = prefetchConnectionTask;
            prefetchConnectionTask = null;

            await HandleHttpSessionRequest(endPoint, clientStream, cancellationTokenSource, connectArgs, prefetchTask);
        }
        catch (ServerException e)
        {
            closeServerConnection = true;
            OnException(clientStream, e);
        }
        catch (IOException e)
        {
            closeServerConnection = true;
            OnException(clientStream, new Exception("Connection was aborted", e));
        }
        catch (SocketException e)
        {
            closeServerConnection = true;
            OnException(clientStream, new Exception("Could not connect", e));
        }
        catch (Exception e)
        {
            closeServerConnection = true;
            OnException(clientStream, new Exception("Error occured in whilst handling the client", e));
        }
        finally
        {
            if (!cancellationTokenSource.IsCancellationRequested) cancellationTokenSource.Cancel();
            await TcpConnectionFactory.Release(prefetchConnectionTask, closeServerConnection);
            clientStream.Dispose();
            connectArgs?.Dispose();
        }
    }
}