

using System;
using System.Net;
using System.Net.Sockets;

namespace T2Proxy.ProxySocket.Authentication;

internal abstract class AuthMethod
{
    
    protected HandShakeComplete CallBack;

    private Socket server;

    public AuthMethod(Socket server)
    {
        Server = server;
    }

    protected Socket Server
    {
        get => server;
        set => server = value ?? throw new ArgumentNullException();
    }
    protected byte[] Buffer { get; set; }

    protected int Received { get; set; }

    public abstract void Authenticate();

    public abstract void BeginAuthenticate(HandShakeComplete callback);
}