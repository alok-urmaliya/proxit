using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using T2Proxy.Helpers;
using T2Proxy.Models;

namespace T2Proxy.Network.Tcp;

internal class TcpServerConnection : IDisposable
{
    private bool disposed;

    internal TcpServerConnection(ProxyServer proxyServer, Socket tcpSocket, HttpServerStream stream,
        string hostName, int port, bool isHttps, SslApplicationProtocol negotiatedApplicationProtocol,
        Version version, IExternalServer? upStreamProxy, IPEndPoint? upStreamEndPoint, string cacheKey)
    {
        TcpSocket = tcpSocket;
        LastAccess = DateTime.UtcNow;
        ProxyServer = proxyServer;
        ProxyServer.UpdateServerConnectionCount(true);
        Stream = stream;
        HostName = hostName;
        Port = port;
        IsHttps = isHttps;
        NegotiatedApplicationProtocol = negotiatedApplicationProtocol;
        Version = version;
        UpStreamProxy = upStreamProxy;
        UpStreamEndPoint = upStreamEndPoint;

        CacheKey = cacheKey;
    }

    public Guid Id { get; } = Guid.NewGuid();

    private ProxyServer ProxyServer { get; }

    internal bool IsClosed => Stream.IsClosed;

    internal IExternalServer? UpStreamProxy { get; set; }

    internal string HostName { get; set; }

    internal int Port { get; set; }

    internal bool IsHttps { get; set; }

    internal SslApplicationProtocol NegotiatedApplicationProtocol { get; set; }

    internal IPEndPoint? UpStreamEndPoint { get; set; }

    internal Version Version { get; set; } = HttpHeader.VersionUnknown;

    internal Socket TcpSocket { get; }

    internal HttpServerStream Stream { get; }
    internal DateTime LastAccess { get; set; }
    internal string CacheKey { get; set; }

    internal bool IsWinAuthenticated { get; set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        Task.Run(async () =>
        {
            await Task.Delay(1000);

            ProxyServer.UpdateServerConnectionCount(false);

            if (disposing)
            {
                Stream.Dispose();

                try
                {
                    TcpSocket.Close();
                }
                catch
                {
                    // ignore
                }
            }
        });

        disposed = true;
    }

    ~TcpServerConnection()
    {
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}