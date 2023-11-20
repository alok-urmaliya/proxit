using Org.BouncyCastle.Utilities.Encoders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.ProxySocket;

namespace T2Proxy.Network.Tcp;

internal class TcpConnectionFactory : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TcpServerConnection>> cache = new();

    private readonly ConcurrentBag<TcpServerConnection> disposalBag = new();

    private readonly SemaphoreSlim @lock = new(1);

    private bool disposed;

    private volatile bool runCleanUpTask = true;

    internal TcpConnectionFactory(ProxyServer server)
    {
        Server = server;
        Task.Run(async () => await ClearOutdatedConnections());
    }

    internal ProxyServer Server { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal string GetConnectionCacheKey(string remoteHostName, int remotePort,
        bool isHttps, List<SslApplicationProtocol>? applicationProtocols,
        IPEndPoint? upStreamEndPoint, IExternalServer? externalProxy)
    {
        var cacheKeyBuilder = new StringBuilder();
        cacheKeyBuilder.Append(remoteHostName);
        cacheKeyBuilder.Append("-");
        cacheKeyBuilder.Append(remotePort);
        cacheKeyBuilder.Append("-");
        cacheKeyBuilder.Append(isHttps);

        if (applicationProtocols != null)
            foreach (var protocol in applicationProtocols.OrderBy(x => x))
            {
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(protocol);
            }

        if (upStreamEndPoint != null)
        {
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(upStreamEndPoint.Address);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(upStreamEndPoint.Port);
        }

        if (externalProxy != null)
        {
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.HostName);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.Port);
            cacheKeyBuilder.Append("-");
            cacheKeyBuilder.Append(externalProxy.ProxyType);

            if (externalProxy.UseDefaultCredentials)
            {
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(externalProxy.UserName);
                cacheKeyBuilder.Append("-");
                cacheKeyBuilder.Append(externalProxy.Password);
            }
        }

        return cacheKeyBuilder.ToString();
    }

    internal async Task<string> GetConnectionCacheKey(ProxyServer server, SessionEventArgsBase session,
        SslApplicationProtocol applicationProtocol)
    {
        List<SslApplicationProtocol>? applicationProtocols = null;
        if (applicationProtocol != default)
            applicationProtocols = new List<SslApplicationProtocol> { applicationProtocol };

        var customUpStreamProxy = session.CustomUpStreamProxy;

        var isHttps = session.IsHttps;
        if (customUpStreamProxy == null && server.GetCustomUpStreamProxyFunc != null)
            customUpStreamProxy = await server.GetCustomUpStreamProxyFunc(session);

        session.CustomUpStreamProxyUsed = customUpStreamProxy;

        var uri = session.HttpClient.Request.RequestUri;
        var upStreamEndPoint = session.HttpClient.UpStreamEndPoint ?? server.UpStreamEndPoint;
        var upStreamProxy = customUpStreamProxy ?? (isHttps ? server.UpStreamHttpsProxy : server.UpStreamHttpProxy);
        return GetConnectionCacheKey(uri.Host, uri.Port, isHttps, applicationProtocols, upStreamEndPoint,
            upStreamProxy);
    }


    internal Task<TcpServerConnection> GetServerConnection(ProxyServer proxyServer, SessionEventArgsBase session,
        bool isConnect,
        SslApplicationProtocol applicationProtocol, bool noCache, CancellationToken cancellationToken)
    {
        List<SslApplicationProtocol>? applicationProtocols = null;
        if (applicationProtocol != default)
            applicationProtocols = new List<SslApplicationProtocol> { applicationProtocol };

        return GetServerConnection(proxyServer, session, isConnect, applicationProtocols, noCache, false,
            cancellationToken)!;
    }

    internal async Task<TcpServerConnection?> GetServerConnection(ProxyServer proxyServer, SessionEventArgsBase session,
        bool isConnect,
        List<SslApplicationProtocol>? applicationProtocols, bool noCache, bool prefetch,
        CancellationToken cancellationToken)
    {
        var customUpStreamProxy = session.CustomUpStreamProxy;

        var isHttps = session.IsHttps;
        if (customUpStreamProxy == null && proxyServer.GetCustomUpStreamProxyFunc != null)
            customUpStreamProxy = await proxyServer.GetCustomUpStreamProxyFunc(session);

        session.CustomUpStreamProxyUsed = customUpStreamProxy;

        var request = session.HttpClient.Request;
        string host;
        int port;
        if (request.Authority.Length > 0)
        {
            var authority = request.Authority;
            var idx = authority.IndexOf((byte)':');
            if (idx == -1)
            {
                host = authority.GetString();
                port = 80;
            }
            else
            {
                host = authority.Slice(0, idx).GetString();
                port = int.Parse(authority.Slice(idx + 1).GetString());
            }
        }
        else
        {
            var uri = request.RequestUri;
            host = uri.Host;
            port = uri.Port;
        }

        var upStreamEndPoint = session.HttpClient.UpStreamEndPoint ?? proxyServer.UpStreamEndPoint;
        var upStreamProxy = customUpStreamProxy ??
                            (isHttps ? proxyServer.UpStreamHttpsProxy : proxyServer.UpStreamHttpProxy);
        return await GetServerConnection(proxyServer, host, port, session.HttpClient.Request.HttpVersion, isHttps,
            applicationProtocols, isConnect, session, upStreamEndPoint, upStreamProxy, noCache, prefetch,
            cancellationToken);
    }

    internal async Task<TcpServerConnection?> GetServerConnection(ProxyServer proxyServer, string remoteHostName,
        int remotePort,
        Version httpVersion, bool isHttps, List<SslApplicationProtocol>? applicationProtocols, bool isConnect,
        SessionEventArgsBase sessionArgs, IPEndPoint? upStreamEndPoint, IExternalServer? externalProxy,
        bool noCache, bool prefetch, CancellationToken cancellationToken)
    {
        var sslProtocol = sessionArgs.ClientConnection.SslProtocol;
        var cacheKey = GetConnectionCacheKey(remoteHostName, remotePort,
            isHttps, applicationProtocols, upStreamEndPoint, externalProxy);

        if (proxyServer.EnableConnectionPool && !noCache)
            if (cache.TryGetValue(cacheKey, out var existingConnections))
                lock (existingConnections)
                {
                    var cutOff = DateTime.UtcNow.AddSeconds(-proxyServer.ConnectionTimeOutSeconds + 3);
                    while (existingConnections.Count > 0)
                        if (existingConnections.TryDequeue(out var recentConnection))
                        {
                            if (recentConnection.LastAccess > cutOff
                                && recentConnection.TcpSocket.IsGoodConnection())
                                return recentConnection;

                            disposalBag.Add(recentConnection);
                        }
                }

        var connection = await CreateServerConnection(remoteHostName, remotePort, httpVersion, isHttps, sslProtocol,
            applicationProtocols, isConnect, proxyServer, sessionArgs, upStreamEndPoint, externalProxy, cacheKey,
            prefetch, cancellationToken);

        return connection;
    }

    private async Task<TcpServerConnection?> CreateServerConnection(string remoteHostName, int remotePort,
        Version httpVersion, bool isHttps, SslProtocols sslProtocol, List<SslApplicationProtocol>? applicationProtocols,
        bool isConnect,
        ProxyServer proxyServer, SessionEventArgsBase sessionArgs, IPEndPoint? upStreamEndPoint,
        IExternalServer? externalProxy, string cacheKey,
        bool prefetch, CancellationToken cancellationToken)
    {
        if (Server.ProxyEndPoints.Any(x => x.Port == remotePort)
            && NetworkHelper.IsLocalIpAddress(remoteHostName))
            throw new Exception(
                $"A client is making HTTP request to one of the listening ports of this proxy {remoteHostName}:{remotePort}");

        if (externalProxy != null)
            if (Server.ProxyEndPoints.Any(x => x.Port == externalProxy.Port)
                && NetworkHelper.IsLocalIpAddress(externalProxy.HostName))
                throw new Exception(
                    $"A client is making HTTP request via external proxy to one of the listening ports of this proxy {remoteHostName}:{remotePort}");

        if (proxyServer.SupportedServerSslProtocols != SslProtocols.None) sslProtocol = proxyServer.SupportedServerSslProtocols;

        if (isHttps && sslProtocol == SslProtocols.None) sslProtocol = proxyServer.SupportedSslProtocols;

        var useUpstreamProxy1 = false;

        if (externalProxy != null && !(externalProxy.HostName == remoteHostName && externalProxy.Port == remotePort))
        {
            useUpstreamProxy1 = true;

            if (externalProxy.BypassLocalhost &&
                NetworkHelper.IsLocalIpAddress(remoteHostName, externalProxy.ProxyDnsRequests))
                useUpstreamProxy1 = false;
        }

        if (!useUpstreamProxy1) externalProxy = null;

        Socket? tcpServerSocket = null;
        HttpServerStream? stream = null;

        SslApplicationProtocol negotiatedApplicationProtocol = default;

        var retry = true;
        var enabledSslProtocols = sslProtocol;

        retry:
        try
        {
            var socks = externalProxy != null && externalProxy.ProxyType != ExternalProxyType.Http;
            var hostname = remoteHostName;
            var port = remotePort;

            if (externalProxy != null)
            {
                hostname = externalProxy.HostName;
                port = externalProxy.Port;
            }

            var ipAddresses = await Dns.GetHostAddressesAsync(hostname);
            if (ipAddresses == null || ipAddresses.Length == 0)
            {
                if (prefetch) return null;

                throw new Exception($"Could not resolve the hostname {hostname}");
            }

            if (sessionArgs != null) sessionArgs.TimeLine["Dns Resolved"] = DateTime.UtcNow;

            Array.Sort(ipAddresses, (x, y) => x.AddressFamily.CompareTo(y.AddressFamily));

            Exception? lastException = null;
            for (var i = 0; i < ipAddresses.Length; i++)
                try
                {
                    var ipAddress = ipAddresses[i];
                    var addressFamily = upStreamEndPoint?.AddressFamily ?? ipAddress.AddressFamily;

                    if (socks)
                    {
                        var proxySocket =
                            new ProxySocket.ServerSocket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                        proxySocket.ProxyType = externalProxy!.ProxyType == ExternalProxyType.Socks4
                            ? ProxyTypes.Socks4
                            : ProxyTypes.Socks5;

                        proxySocket.ProxyEndPoint = new IPEndPoint(ipAddress, port);
                        if (!string.IsNullOrEmpty(externalProxy.UserName) && externalProxy.Password != null)
                        {
                            proxySocket.ProxyUser = externalProxy.UserName;
                            proxySocket.ProxyPass = externalProxy.Password;
                        }

                        tcpServerSocket = proxySocket;
                    }
                    else
                    {
                        tcpServerSocket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                    }

                    if (upStreamEndPoint != null) tcpServerSocket.Bind(upStreamEndPoint);

                    tcpServerSocket.NoDelay = proxyServer.NoDelay;
                    tcpServerSocket.ReceiveTimeout = proxyServer.ConnectionTimeOutSeconds * 1000;
                    tcpServerSocket.SendTimeout = proxyServer.ConnectionTimeOutSeconds * 1000;
                    tcpServerSocket.LingerState = new LingerOption(true, proxyServer.TcpTimeWaitSeconds);

                    if (proxyServer.ReuseSocket && RunTime.IsSocketReuseAvailable())
                        tcpServerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    Task connectTask;

                    if (socks)
                    {
                        if (externalProxy!.ProxyDnsRequests)
                        {
                            connectTask =
                                ProxySocketConnectionTaskFactory.CreateTask((ProxySocket.ServerSocket)tcpServerSocket,
                                    remoteHostName, remotePort);
                        }
                        else
                        {
                            var remoteIpAddresses = await Dns.GetHostAddressesAsync(remoteHostName);
                            if (remoteIpAddresses == null || remoteIpAddresses.Length == 0)
                                throw new Exception($"Could not resolve the SOCKS remote hostname {remoteHostName}");
                            connectTask = ProxySocketConnectionTaskFactory.CreateTask(
                                (ProxySocket.ServerSocket)tcpServerSocket, remoteIpAddresses[0], remotePort);
                        }
                    }
                    else
                    {
                        connectTask = SocketConnectionTaskFactory.CreateTask(tcpServerSocket, ipAddress, port);
                    }

                    await Task.WhenAny(connectTask,
                        Task.Delay(proxyServer.ConnectTimeOutSeconds * 1000, cancellationToken));
                    if (!connectTask.IsCompleted || !tcpServerSocket.Connected)
                    {
                        try
                        {
                            connectTask.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }

                        try
                        {
                            tcpServerSocket?.Dispose();
                            tcpServerSocket = null;
                        }
                        catch
                        {
                            // ignore
                        }

                        continue;
                    }

                    break;
                }
                catch (Exception e)
                {
                    lastException = e;
                    tcpServerSocket?.Dispose();
                    tcpServerSocket = null;
                }

            if (tcpServerSocket == null)
            {
                if (sessionArgs != null && proxyServer.CustomUpStreamProxyFailureFunc != null)
                {
                    var newUpstreamProxy = await proxyServer.CustomUpStreamProxyFailureFunc(sessionArgs);
                    if (newUpstreamProxy != null)
                    {
                        sessionArgs.CustomUpStreamProxyUsed = newUpstreamProxy;
                        sessionArgs.TimeLine["Retrying Upstream Proxy Connection"] = DateTime.UtcNow;
                        return await CreateServerConnection(remoteHostName, remotePort, httpVersion, isHttps,
                            sslProtocol, applicationProtocols, isConnect, proxyServer, sessionArgs, upStreamEndPoint,
                            externalProxy, cacheKey, prefetch, cancellationToken);
                    }
                }

                if (prefetch) return null;

                throw new Exception($"Could not establish connection to {hostname}", lastException);
            }

            if (sessionArgs != null) sessionArgs.TimeLine["Connection Established"] = DateTime.UtcNow;

            await proxyServer.InvokeServerConnectionCreateEvent(tcpServerSocket);

            stream = new HttpServerStream(proxyServer, new NetworkStream(tcpServerSocket, true), proxyServer.BufferPool,
                cancellationToken);

            if (externalProxy != null && externalProxy.ProxyType == ExternalProxyType.Http && (isConnect || isHttps))
            {
                var authority = $"{remoteHostName}:{remotePort}";
                var authorityBytes = authority.GetByteString();
                var connectRequest = new ConnectRequest(authorityBytes)
                {
                    IsHttps = isHttps,
                    RequestUriString8 = authorityBytes,
                    HttpVersion = httpVersion
                };

                connectRequest.Headers.AddHeader(KnownHeaders.Connection, KnownHeaders.ConnectionKeepAlive);
                connectRequest.Headers.AddHeader(KnownHeaders.Host, authority);

                if (!string.IsNullOrEmpty(externalProxy.UserName) && externalProxy.Password != null)
                {
                    connectRequest.Headers.AddHeader(HttpHeader.ProxyConnectionKeepAlive);
                    connectRequest.Headers.AddHeader(
                        HttpHeader.GetProxyAuthorizationHeader(externalProxy.UserName, externalProxy.Password));
                }

                await proxyServer.OnBeforeUpStreamConnectRequest(connectRequest);

                await stream.WriteRequestAsync(connectRequest, cancellationToken);

                var httpStatus = await stream.ReadResponseStatus(cancellationToken);
                var headers = new HeaderCollection();
                await HeaderParser.ReadHeaders(stream, headers, cancellationToken);

                if (httpStatus.StatusCode != 200 && !httpStatus.Description.EqualsIgnoreCase("OK")
                                                 && !httpStatus.Description.EqualsIgnoreCase("Connection Established"))
                    throw new Exception("Upstream proxy failed to create a secure tunnel");
            }

            if (isHttps)
            {
                var sslStream = new SslStream(stream, false,
                    (sender, certificate, chain, sslPolicyErrors) =>
                        proxyServer.ValidateServerCertificate(sender, sessionArgs, certificate, chain,
                            sslPolicyErrors),
                    (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                        proxyServer.SelectClientCertificate(sender, sessionArgs, targetHost, localCertificates,
                            remoteCertificate, acceptableIssuers));
                stream = new HttpServerStream(proxyServer, sslStream, proxyServer.BufferPool, cancellationToken);

                var options = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = applicationProtocols,
                    TargetHost = remoteHostName,
                    ClientCertificates = null!,
                    EnabledSslProtocols = enabledSslProtocols,
                    CertificateRevocationCheckMode = proxyServer.CheckCertificateRevocation
                };
                await sslStream.AuthenticateAsClientAsync(options, cancellationToken);
#if NET6_0_OR_GREATER
                negotiatedApplicationProtocol = sslStream.NegotiatedApplicationProtocol;
#endif

                if (sessionArgs != null) sessionArgs.TimeLine["HTTPS Established"] = DateTime.UtcNow;
            }
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80131620) && retry &&
                                     enabledSslProtocols >= SslProtocols.Tls11)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();

            enabledSslProtocols = proxyServer.SupportedSslProtocols & (SslProtocols)0xff;

            if (enabledSslProtocols == SslProtocols.None) throw;

            retry = false;
            goto retry;
        }
        catch (AuthenticationException ex) when (ex.HResult == unchecked((int)0x80131501) && retry &&
                                                 enabledSslProtocols >= SslProtocols.Tls11)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();
            enabledSslProtocols = proxyServer.SupportedSslProtocols & (SslProtocols)0xff;

            if (enabledSslProtocols == SslProtocols.None) throw;

            retry = false;
            goto retry;
        }
        catch (Exception)
        {
            stream?.Dispose();
            tcpServerSocket?.Close();
            throw;
        }

        return new TcpServerConnection(proxyServer, tcpServerSocket, stream, remoteHostName, remotePort, isHttps,
            negotiatedApplicationProtocol, httpVersion, externalProxy, upStreamEndPoint, cacheKey);
    }

    internal async Task Release(TcpServerConnection? connection, bool close = false)
    {
        if (connection == null) return;

        if (disposalBag.Any(x => x == connection)) return;

        if (close || connection.IsWinAuthenticated || !Server.EnableConnectionPool || connection.IsClosed)
        {
            disposalBag.Add(connection);
            return;
        }

        connection.LastAccess = DateTime.UtcNow;

        try
        {
            await @lock.WaitAsync();

            while (true)
            {
                if (cache.TryGetValue(connection.CacheKey, out var existingConnections))
                {
                    while (existingConnections.Count >= Server.MaxCachedConnections)
                        if (existingConnections.TryDequeue(out var staleConnection))
                            disposalBag.Add(staleConnection);

                    if (existingConnections.Any(x => x == connection)) break;

                    existingConnections.Enqueue(connection);
                    break;
                }

                if (cache.TryAdd(connection.CacheKey,
                        new ConcurrentQueue<TcpServerConnection>(new[] { connection })))
                    break;
            }
        }
        finally
        {
            @lock.Release();
        }
    }

    internal async Task Release(Task<TcpServerConnection?>? connectionCreateTask, bool closeServerConnection)
    {
        if (connectionCreateTask == null) return;

        TcpServerConnection? connection = null;
        try
        {
            connection = await connectionCreateTask;
        }
        catch
        {
            // ignore
        }
        finally
        {
            if (connection != null) await Release(connection, closeServerConnection);
        }
    }

    private async Task ClearOutdatedConnections()
    {
        while (runCleanUpTask)
            try
            {
                var cutOff = DateTime.UtcNow.AddSeconds(-Server.ConnectionTimeOutSeconds);
                foreach (var item in cache)
                {
                    var queue = item.Value;

                    while (queue.Count > 0)
                        if (queue.TryDequeue(out var connection))
                        {
                            if (!Server.EnableConnectionPool || connection.LastAccess < cutOff)
                            {
                                disposalBag.Add(connection);
                            }
                            else
                            {
                                queue.Enqueue(connection);
                                break;
                            }
                        }
                }

                try
                {
                    await @lock.WaitAsync();

                    var emptyKeys = cache.ToArray().Where(x => x.Value.Count == 0).Select(x => x.Key);
                    foreach (var key in emptyKeys) cache.TryRemove(key, out _);
                }
                finally
                {
                    @lock.Release();
                }

                while (!disposalBag.IsEmpty)
                    if (disposalBag.TryTake(out var connection))
                        connection?.Dispose();
            }
            catch (Exception e)
            {
                Server.ExceptionFunc?.Invoke(new Exception("An error occurred when disposing server connections.", e));
            }
            finally
            {
                await Task.Delay(1000 * 3);
            }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        runCleanUpTask = false;

        if (disposing)
        {
            try
            {
                @lock.Wait();

                foreach (var queue in cache.Select(x => x.Value).ToList())
                    while (!queue.IsEmpty)
                        if (queue.TryDequeue(out var connection))
                            disposalBag.Add(connection);

                cache.Clear();
            }
            finally
            {
                @lock.Release();
            }

            while (!disposalBag.IsEmpty)
                if (disposalBag.TryTake(out var connection))
                    connection?.Dispose();
        }

        disposed = true;
    }

    ~TcpConnectionFactory()
    {
        Dispose(false);
    }

    private static class SocketConnectionTaskFactory
    {
        private static IAsyncResult BeginConnect(IPAddress address, int port, AsyncCallback requestCallback,
            object state)
        {
            return ((Socket)state).BeginConnect(address, port, requestCallback, state);
        }

        private static void EndConnect(IAsyncResult asyncResult)
        {
                ((Socket)asyncResult.AsyncState).EndConnect(asyncResult);
        }

        public static Task CreateTask(Socket socket, IPAddress ipAddress, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, ipAddress, port, socket);
        }
    }

    private static class ProxySocketConnectionTaskFactory
    {
        private static IAsyncResult BeginConnect(IPAddress address, int port, AsyncCallback requestCallback,
            object state)
        {
            return ((ProxySocket.ServerSocket)state).BeginConnect(address, port, requestCallback, state);
        }

        private static IAsyncResult BeginConnect(string hostName, int port, AsyncCallback requestCallback, object state)
        {
            return ((ProxySocket.ServerSocket)state).BeginConnect(hostName, port, requestCallback, state);
        }

        private static void EndConnect(IAsyncResult asyncResult)
        {
            ((ProxySocket.ServerSocket)asyncResult.AsyncState).EndConnect(asyncResult);
        }

        public static Task CreateTask(ProxySocket.ServerSocket socket, IPAddress ipAddress, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, ipAddress, port, socket);
        }

        public static Task CreateTask(ProxySocket.ServerSocket socket, string hostName, int port)
        {
            return Task.Factory.FromAsync(BeginConnect, EndConnect, hostName, port, socket);
        }
    }
}