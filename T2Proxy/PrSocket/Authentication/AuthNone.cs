

using System.Net.Sockets;

namespace T2Proxy.ProxySocket.Authentication;

internal sealed class AuthNone : AuthMethod
{
    public AuthNone(Socket server) : base(server)
    {
    }

    public override void Authenticate()
    {
    }
    public override void BeginAuthenticate(HandShakeComplete callback)
    {
        callback(null);
    }
}