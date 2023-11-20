using T2Proxy.Models;
using T2Proxy.StreamExtended;

namespace T2Proxy.Http;

public class ConnectRequest : Request
{
    internal ConnectRequest(ByteStream authority)
    {
        Method = "CONNECT";
        Authority = authority;
    }
    public TunnelType TunnelType { get; internal set; }
    public ClientHelloInfo? ClientHelloInfo { get; set; }
}