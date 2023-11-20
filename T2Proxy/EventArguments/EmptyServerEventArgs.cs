using T2Proxy.Network.Tcp;

namespace T2Proxy.EventArguments;

public class EmptyServerEventArgs : ServerEventArgsBase
{
    internal EmptyServerEventArgs(ProxyServer server, TcpClientConnection clientConnection) : base(server,
        clientConnection)
    {
    }
}