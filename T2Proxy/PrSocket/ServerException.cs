using System;

namespace T2Proxy.ProxySocket;
[Serializable]
internal class ServerException : Exception
{
    public ServerException() : this("An error occured while talking to the proxy server.")
    {
    }
    public ServerException(string message) : base(message)
    {
    }
    public ServerException(int socks5Error) : this(Socks5ToString(socks5Error))
    {
    }
    public static string Socks5ToString(int socks5Error)
    {
        switch (socks5Error)
        {
            case 0:
                return "Connection succeeded.";
            case 1:
                return "General SOCKS server failure.";
            case 2:
                return "Connection not allowed by ruleset.";
            case 3:
                return "Network unreachable.";
            case 4:
                return "Host unreachable.";
            case 5:
                return "Connection refused.";
            case 6:
                return "TTL expired.";
            case 7:
                return "Command not supported.";
            case 8:
                return "Address type not supported.";
            default:
                return "Unspecified SOCKS error.";
        }
    }
}