using System;
using T2Proxy.Network.Tcp;

namespace T2Proxy.EventArguments;

public abstract class ServerEventArgsBase : EventArgs
{
    private readonly TcpClientConnection clientConnection;
    internal readonly ProxyServer Server;

    internal ServerEventArgsBase(ProxyServer server, TcpClientConnection clientConnection)
    {
        this.clientConnection = clientConnection;
        Server = server;
    }

    public object ClientUserData
    {
        get => clientConnection.ClientUserData;
        set => clientConnection.ClientUserData = value;
    }
}