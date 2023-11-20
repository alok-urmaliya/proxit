using System;
using System.Threading;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.EventArguments;
public class TunnelConnectSessionEventArgs : SessionEventArgsBase
{
    private bool? isHttpsConnect;

    internal TunnelConnectSessionEventArgs(ProxyServer server, ServerEndPoint endPoint, ConnectRequest connectRequest,
        HttpClientStream clientStream, CancellationTokenSource cancellationTokenSource)
        : base(server, endPoint, clientStream, connectRequest, connectRequest, cancellationTokenSource)
    {
    }
    public bool DecryptSsl { get; set; } = true;
    public bool DenyConnect { get; set; }
    public bool IsHttpsConnect
    {
        get => isHttpsConnect ??
               throw new Exception("The value of this property is known in the BeforeTunnelConnectResponse event");

        internal set => isHttpsConnect = value;
    }
    public event EventHandler<DataEventArgs>? DecryptedDataSent;
    public event EventHandler<DataEventArgs>? DecryptedDataReceived;

    internal void OnDecryptedDataSent(byte[] buffer, int offset, int count)
    {
        try
        {
            DecryptedDataSent?.Invoke(this, new DataEventArgs(buffer, offset, count));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }

    internal void OnDecryptedDataReceived(byte[] buffer, int offset, int count)
    {
        try
        {
            DecryptedDataReceived?.Invoke(this, new DataEventArgs(buffer, offset, count));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }

    ~TunnelConnectSessionEventArgs()
    {
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
}