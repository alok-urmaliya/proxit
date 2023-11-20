using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using T2Proxy.Helpers;
using T2Proxy.Models;

namespace T2Proxy.Network.Tcp;

internal class TcpClientConnection : IDisposable
{
    private readonly Socket tcpClientSocket;

    private bool disposed;

    private int? processId;

    internal TcpClientConnection(ProxyServer proxyServer, Socket tcpClientSocket)
    {
        this.tcpClientSocket = tcpClientSocket;
        ProxyServer = proxyServer;
        ProxyServer.UpdateClientConnectionCount(true);
    }

    public object ClientUserData { get; set; }

    private ProxyServer ProxyServer { get; }

    public Guid Id { get; } = Guid.NewGuid();

    public EndPoint LocalEndPoint => tcpClientSocket.LocalEndPoint;

    public EndPoint RemoteEndPoint => tcpClientSocket.RemoteEndPoint;

    internal SslProtocols SslProtocol { get; set; }

    internal SslApplicationProtocol NegotiatedApplicationProtocol { get; set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Stream GetStream()
    {
        return new NetworkStream(tcpClientSocket, true);
    }

    public int GetProcessId(ServerEndPoint endPoint)
    {
        if (processId.HasValue) return processId.Value;

        if (RunTime.IsWindows)
        {
            var remoteEndPoint = (IPEndPoint)RemoteEndPoint;

            if (NetworkHelper.IsLocalIpAddress(remoteEndPoint.Address))
                processId = TCPHelper.GetProcessIdByLocalPort(endPoint.IpAddress.AddressFamily, remoteEndPoint.Port);
            else
                processId = -1;

            return processId.Value;
        }

        throw new PlatformNotSupportedException();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        Task.Run(async () =>
        {
            await Task.Delay(1000);
            ProxyServer.UpdateClientConnectionCount(false);

            if (disposing)
                try
                {
                    tcpClientSocket.Close();
                }
                catch
                {
                    // ignore
                }
        });

        disposed = true;
    }

    ~TcpClientConnection()
    {
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}